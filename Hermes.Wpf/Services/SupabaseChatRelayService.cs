using Hermes.Wpf.Models;
using Supabase;
using Supabase.Gotrue.Exceptions;

namespace Hermes.Wpf.Services;

/// <summary>Minimal Supabase client for the shared <c>messages</c> table (voice / Hermes relay).</summary>
public sealed class SupabaseChatRelayService
{
    private readonly LogService _log;
    private readonly HermesSettings _settings;
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

        _client = new Client(url, anonKey);
        await _client.InitializeAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        _log.LogInfo($"[supabase] PostgREST client ready (host={host}).");
    }

    /// <summary>Creates an anonymous JWT when the Dashboard provider is enabled (matches DesktopVoiceChat).</summary>
    public async Task EnsureAnonymousSessionAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var existing = _client!.Auth.CurrentUser;
        if (existing is { Id.Length: > 0 })
        {
            _log.LogInfo($"[supabase] Anonymous session already present (user id prefix={ShortId(existing.Id)}).");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _log.LogInfo("[supabase] Anonymous sign-in (GoTrue) …");
            await _client.Auth.SignInAnonymously();
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

        var user = _client.Auth.CurrentUser;
        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            throw new InvalidOperationException(
                "После анонимного входа сессия без user id. Проверьте URL и anon key.");
        }

        _log.LogInfo("[supabase] Anonymous session OK.");
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
