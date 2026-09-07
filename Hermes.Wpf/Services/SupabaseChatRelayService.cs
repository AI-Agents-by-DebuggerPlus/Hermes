using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Hermes.Wpf.Models;
using Supabase;
using Supabase.Gotrue.Exceptions;

namespace Hermes.Wpf.Services;

/// <summary>Minimal Supabase client for the shared <c>messages</c> table (voice / Hermes relay).</summary>
public sealed class SupabaseChatRelayService
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
    private readonly HttpClient _http = new();
    private Client? _client;

    public SupabaseChatRelayService(LogService log, HermesSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsConnected => _client is not null;

    public string? CurrentUserId => _client?.Auth.CurrentSession?.User?.Id ?? _client?.Auth.CurrentUser?.Id;

    public async Task ConnectAsync(string url, string anonKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(anonKey))
        {
            throw new InvalidOperationException("Supabase URL and anon key are required.");
        }

        var host = LogRedaction.SupabaseHostForLog(url);
        _log.LogInfo(
            $"[supabase] Connecting host={host}, anon_key={LogRedaction.MaskApiKey(anonKey)} …");

        // SDK Realtime handshake often returns 403; Phoenix WebSocket is used instead.
        _client = new Client(url, anonKey, new SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = true,
        });
        await _client.InitializeAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _log.LogInfo($"[supabase] PostgREST client ready (host={host}).");
    }

    /// <summary>Creates an anonymous JWT when the Dashboard provider is enabled (matches DesktopVoiceChat).</summary>
    /// <param name="skipKeepFresh">
    /// When true, do not try RefreshSession first (used after an expired refresh already failed,
    /// to avoid a second identical WARN before SignInAnonymously).
    /// </param>
    public async Task EnsureAnonymousSessionAsync(
        CancellationToken cancellationToken = default,
        bool skipKeepFresh = false)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();

        if (!skipKeepFresh && await TryKeepFreshSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            // Drop expired JWT so AutoRefreshToken / RefreshSession cannot race SignInAnonymously.
            await _client!.Auth.SignOut().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogInfo("[supabase] SignOut before anonymous re-login: " + ex.Message);
        }

        try
        {
            _log.LogInfo("[supabase] Anonymous sign-in (GoTrue) …");
            await _client!.Auth.SignInAnonymously();
        }
        catch (GotrueException ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Content)
                ? ex.Message
                : $"{ex.Message} HTTP {(int?)ex.StatusCode}";
            _log.LogError($"[supabase] Anonymous sign-in (GoTrue): {detail}");
            throw new InvalidOperationException(
                "Анонимный вход отклонён Supabase. Включите Authentication → Providers → Anonymous; " +
                $"ответ: {ex.Message}",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var user = _client!.Auth.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            throw new InvalidOperationException(
                "После анонимного входа сессия без user id. Проверьте URL и anon key.");
        }

        _log.LogInfo("[supabase] Anonymous session OK.");
    }

    /// <summary>
    /// Refresh or re-issue JWT before PostgREST writes. Long-lived Hermes.Wpf sessions otherwise
    /// hit <c>PGRST303 JWT expired</c> while WebSocket (anon apikey) still looks healthy.
    /// </summary>
    public async Task EnsureFreshSessionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();

        if (await TryKeepFreshSessionAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // No usable session — anonymous re-login (same path as DesktopVoiceChat).
        _log.LogWarn("[supabase] Нет свежей сессии — повторный anonymous sign-in.");
        await EnsureAnonymousSessionAsync(cancellationToken, skipKeepFresh: true).ConfigureAwait(false);
    }

    private async Task<bool> TryKeepFreshSessionAsync(CancellationToken cancellationToken)
    {
        var session = _client!.Auth.CurrentSession;
        var user = session?.User ?? _client.Auth.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return false;
        }

        // Expired() is true at/after ExpiresAt; refresh a minute early to avoid race on insert.
        var expiredOrNear = session is null
                            || session.Expired()
                            || session.ExpiresAt() <= DateTime.UtcNow.AddMinutes(1);
        if (!expiredOrNear)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(session?.RefreshToken))
        {
            try
            {
                _log.LogInfo("[supabase] Access token истёк/скоро истечёт — RefreshSession …");
                await _client.Auth.RefreshSession();
                var refreshed = _client.Auth.CurrentSession;
                if (refreshed is not null && !refreshed.Expired() &&
                    !string.IsNullOrWhiteSpace(refreshed.User?.Id ?? _client.Auth.CurrentUser?.Id))
                {
                    _log.LogInfo("[supabase] Session refreshed OK.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[supabase] RefreshSession не удался: {ex.Message}");
            }
        }
        else
        {
            _log.LogWarn("[supabase] Access token истёк, refresh_token отсутствует.");
        }

        return false;
    }

    public async Task<IReadOnlyList<SupabaseMessageRow>> FetchAllSortedAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _client!.From<SupabaseMessageRow>().Get(cancellationToken);
        return response.Models
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }

    /// <summary>Insert chat line as <see cref="SupabaseHermesEchoTracker"/> consumes echoed Hermes rows.</summary>
    public async Task InsertAssistantRowAsync(
        string senderDisplayName,
        string recipientDisplayName,
        string content,
        CancellationToken cancellationToken = default,
        bool logPublish = true)
    {
        EnsureConnected();
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(false);

        var currentUserId = CurrentUserId
                            ?? throw new InvalidOperationException("Supabase session has no user id.");

        var recipient = string.IsNullOrWhiteSpace(recipientDisplayName) ? "Unknown" : recipientDisplayName.Trim();

        await _client!.From<SupabaseMessageInsertRow>()
            .Insert(new SupabaseMessageInsertRow
                {
                    SenderId = currentUserId,
                    SenderName = senderDisplayName,
                    RecipientName = recipient,
                    Content = content,
                    CreatedAt = _settings.SupabaseUseLocalCreatedAt ? DateTimeOffset.Now : DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken);

        if (logPublish)
        {
            _log.LogInfo(
                $"[supabase] Published row (sender_name={senderDisplayName}, recipient_name={recipient}, chars={content.Length}).");
        }
    }

    /// <summary>
    /// Upload PNG to Storage <c>chat-files</c> and notify RemoteTerminal via <c>hwt_screenshot</c> message.
    /// Falls back to inline base64 if Storage upload fails.
    /// </summary>
    public async Task<(bool Ok, string Message)> PublishHwtScreenshotAsync(
        string localPngPath,
        CancellationToken cancellationToken = default,
        bool replay = false)
    {
        if (string.IsNullOrWhiteSpace(localPngPath) || !File.Exists(localPngPath))
        {
            return (false, "файл скриншота не найден");
        }

        EnsureConnected();
        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(false);
        var uid = CurrentUserId
                  ?? throw new InvalidOperationException("Supabase session has no user id.");

        var fileName = Path.GetFileName(localPngPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "hwt_chart.png";
        }

        var bytes = await File.ReadAllBytesAsync(localPngPath, cancellationToken).ConfigureAwait(false);
        var recipient = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogRecipientName)
            ? "RemoteTerminal"
            : _settings.SupabaseRemoteLogRecipientName.Trim();
        var sender = string.IsNullOrWhiteSpace(_settings.SupabaseRemoteLogSenderName)
            ? "Hermes"
            : _settings.SupabaseRemoteLogSenderName.Trim();

        var nonce = Guid.NewGuid().ToString("N");

        // «повтор»: только сигнал — RT показывает кэш на 10 с.
        // Не шлём второй hwt_screenshot: иначе через ~1 с (после upload) идёт
        // повторный Show и гонка с отложенным SC_MONITORPOWER.
        if (replay)
        {
            var repeatContent = JsonSerializer.Serialize(new
            {
                type = "hwt_screenshot_repeat",
                name = fileName,
                nonce,
                replay = true,
            });
            await InsertAssistantRowAsync(sender, recipient, repeatContent, cancellationToken, logPublish: false)
                .ConfigureAwait(false);
            _log.LogInfo($"[hwt-screenshot] repeat → {recipient} (10s cache show) nonce={nonce}");
            return (true, "повтор → " + recipient);
        }

        string content;
        var storagePath = $"{uid}/hwt-screenshots/{Guid.NewGuid():N}_{fileName}";
        try
        {
            await _client!.Storage
                .From("chat-files")
                .Upload(localPngPath, storagePath, new Supabase.Storage.FileOptions { Upsert = true });
            content = JsonSerializer.Serialize(new
            {
                type = "hwt_screenshot",
                name = fileName,
                bucket = "chat-files",
                path = storagePath,
                mime = "image/png",
                size = bytes.Length,
                nonce,
            });
            _log.LogInfo($"[hwt-screenshot] Storage upload OK → {storagePath}");
        }
        catch (Exception ex)
        {
            _log.LogWarn("[hwt-screenshot] Storage failed, inline base64: " + ex.Message);
            if (bytes.Length > 1_200_000)
            {
                return (false, "Storage недоступен, файл слишком большой для base64");
            }

            content = JsonSerializer.Serialize(new
            {
                type = "hwt_screenshot",
                name = fileName,
                mime = "image/png",
                size = bytes.Length,
                data_base64 = Convert.ToBase64String(bytes),
                nonce,
            });
        }

        await InsertAssistantRowAsync(sender, recipient, content, cancellationToken, logPublish: false)
            .ConfigureAwait(false);
        _log.LogInfo($"[hwt-screenshot] notified → {recipient}");
        return (true, "отправлено → " + recipient);
    }

    /// <summary>
    /// GET Storage object via authenticated path (private <c>chat-files</c>), public URL fallback.
    /// Path segments are URL-encoded; slashes kept.
    /// </summary>
    public async Task<byte[]?> DownloadStorageObjectAsync(
        string bucket,
        string path,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        await EnsureFreshSessionAsync(cancellationToken).ConfigureAwait(false);

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var encodedPath = AndroidChatPhotoPayload.EncodeStorageObjectPath(path);
        var access = _client!.Auth.CurrentSession?.AccessToken;
        if (string.IsNullOrWhiteSpace(access))
        {
            access = anon;
        }

        var url = $"{baseUrl}/storage/v1/object/authenticated/{Uri.EscapeDataString(bucket)}/{encodedPath}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (resp.IsSuccessStatusCode)
        {
            return await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var pub = $"{baseUrl}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{encodedPath}";
        using var req2 = new HttpRequestMessage(HttpMethod.Get, pub);
        req2.Headers.TryAddWithoutValidation("apikey", anon);
        using var resp2 = await _http.SendAsync(req2, cancellationToken).ConfigureAwait(false);
        if (!resp2.IsSuccessStatusCode)
        {
            _log.LogWarn(
                $"[supabase] Storage download fail {(int)resp.StatusCode}/{(int)resp2.StatusCode} bucket={bucket} path={path}");
            return null;
        }

        return await resp2.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        if (_client is null)
        {
            return;
        }

        _log.LogInfo("[supabase] Disconnecting (local client cleared).");
        _client = null;
    }

    private static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "?";
        }

        return id.Length <= 8 ? id : id[..8] + "…";
    }

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Supabase client is not connected.");
        }
    }
}
