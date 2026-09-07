using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hermes.RemoteTerminal.Models;

namespace Hermes.RemoteTerminal.Services;

/// <summary>
/// Auth, INSERT, on-demand Poll (no timer), Storage download.
/// Live channel remains WebSocket only.
/// </summary>
public sealed class SupabaseSessionClient : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private string? _accessToken;
    private string? _userId;
    private Guid? _lastPolledId;

    public SupabaseSessionClient(AppSettings settings)
    {
        _settings = settings;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.SupabaseUrl)
        && !string.IsNullOrWhiteSpace(_settings.SupabaseAnonKey);

    public bool HasUserSession => !string.IsNullOrWhiteSpace(_userId);

    public string? AccessToken => _accessToken;

    public async Task EnsureSessionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_accessToken) && !string.IsNullOrWhiteSpace(_userId))
        {
            return;
        }

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();

        var attempts = new (string Url, string Body)[]
        {
            (baseUrl + "/auth/v1/signup", "{\"data\":{}}"),
            (baseUrl + "/auth/v1/token?grant_type=anonymous", "{}"),
        };

        foreach (var (url, body) in attempts)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("apikey", anon);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", anon);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    AppLog.Warn($"Auth {(int)resp.StatusCode}: {Truncate(text, 80)}");
                    continue;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("access_token", out var tokEl))
                {
                    continue;
                }

                var token = tokEl.GetString();
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                string? uid = null;
                if (root.TryGetProperty("user", out var user)
                    && user.TryGetProperty("id", out var idEl))
                {
                    uid = idEl.GetString();
                }

                uid ??= TryJwtSub(token);
                _accessToken = token;
                _userId = uid;
                AppLog.Info("Supabase session OK uid=" + (uid ?? "?"));
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Auth attempt: " + Truncate(ex.Message, 100));
            }
        }

        _accessToken = anon;
        _userId = null;
        AppLog.Warn("Anonymous auth failed — INSERT/Storage limited");
    }

    public async Task<bool> TryInsertLogAsync(string content, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        await EnsureSessionAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_userId) || string.IsNullOrWhiteSpace(_accessToken))
        {
            return false;
        }

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var payload = JsonSerializer.Serialize(new
        {
            sender_id = _userId,
            sender_name = _settings.LocalSenderName,
            recipient_name = _settings.RecipientName,
            content,
            created_at = DateTimeOffset.Now,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/rest/v1/messages");
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    /// <summary>One-shot command to Hermes.Wpf / HWT (e.g. <c>refresh</c> → <c>Hermes.Mt5Terminal</c>).</summary>
    public async Task<bool> TryInsertCommandAsync(
        string recipientName,
        string content,
        CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(recipientName) || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        await EnsureSessionAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(_userId) || string.IsNullOrWhiteSpace(_accessToken))
        {
            return false;
        }

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var payload = JsonSerializer.Serialize(new
        {
            sender_id = _userId,
            sender_name = _settings.LocalSenderName,
            recipient_name = recipientName.Trim(),
            content = content.Trim(),
            created_at = DateTimeOffset.Now,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/rest/v1/messages");
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            AppLog.Warn($"Command INSERT {(int)resp.StatusCode}: {Truncate(err, 120)}");
        }

        return resp.IsSuccessStatusCode;
    }

    /// <summary>One-shot SELECT of recent messages (explicit Poll / F5 only — no timer).</summary>
    public async Task<IReadOnlyList<TerminalLine>> PollRecentMessagesAsync(CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);
        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var url = baseUrl
                  + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at"
                  + "&order=created_at.desc&limit=30";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken ?? anon);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Poll HTTP {(int)resp.StatusCode}: {Truncate(text, 120)}");
        }

        using var doc = JsonDocument.Parse(text);
        var rows = new List<JsonElement>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(el.Clone());
        }

        if (rows.Count == 0)
        {
            return Array.Empty<TerminalLine>();
        }

        var newestId = Guid.TryParse(GetStr(rows[0], "id"), out var nid) ? nid : (Guid?)null;
        var fresh = new List<JsonElement>();
        foreach (var row in rows)
        {
            Guid.TryParse(GetStr(row, "id"), out var id);
            if (_lastPolledId.HasValue && id == _lastPolledId.Value)
            {
                break;
            }

            fresh.Add(row);
        }

        if (!_lastPolledId.HasValue)
        {
            // First poll: latest status + latest screenshot (no full history dump).
            _lastPolledId = newestId;
            return PickLatestHwtLines(rows);
        }

        if (newestId.HasValue)
        {
            _lastPolledId = newestId;
        }

        fresh.Reverse();
        return fresh
            .Select(ToLine)
            .Where(l => _settings.ShowAllRecipients
                        || string.Equals(l.Recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// One-shot bootstrap: latest <c>hwt_status</c> and latest screenshot for RemoteTerminal.
    /// Not a live poll — call once after connect.
    /// </summary>
    public async Task<IReadOnlyList<TerminalLine>> FetchLatestHwtSnapshotAsync(CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);
        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var recipient = Uri.EscapeDataString(_settings.RecipientName);
        var url = baseUrl
                  + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at"
                  + "&recipient_name=eq." + recipient
                  + "&content=like." + Uri.EscapeDataString("{*")
                  + "&order=created_at.desc&limit=40";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken ?? anon);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bootstrap HTTP {(int)resp.StatusCode}: {Truncate(text, 120)}");
        }

        using var doc = JsonDocument.Parse(text);
        var rows = new List<JsonElement>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            rows.Add(el.Clone());
        }

        if (rows.Count > 0 && Guid.TryParse(GetStr(rows[0], "id"), out var newest))
        {
            _lastPolledId = newest;
        }

        return PickLatestHwtLines(rows, includeScreenshot: false);
    }

    private List<TerminalLine> PickLatestHwtLines(List<JsonElement> rows, bool includeScreenshot = true)
    {
        JsonElement status = default;
        JsonElement shot = default;
        foreach (var row in rows)
        {
            var recipient = GetStr(row, "recipient_name");
            if (!_settings.ShowAllRecipients
                && !string.Equals(recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = GetStr(row, "content");
            if (status.ValueKind == JsonValueKind.Undefined && LooksLikeStatus(content))
            {
                status = row;
            }

            if (includeScreenshot
                && shot.ValueKind == JsonValueKind.Undefined
                && LooksLikeScreenshot(content))
            {
                shot = row;
            }

            if (status.ValueKind != JsonValueKind.Undefined
                && (!includeScreenshot || shot.ValueKind != JsonValueKind.Undefined))
            {
                break;
            }
        }

        var list = new List<TerminalLine>();
        if (status.ValueKind != JsonValueKind.Undefined)
        {
            list.Add(ToLine(status));
        }

        if (shot.ValueKind != JsonValueKind.Undefined)
        {
            list.Add(ToLine(shot));
        }

        return list;
    }

    public void MarkSeen(Guid? id)
    {
        if (id.HasValue)
        {
            _lastPolledId = id;
        }
    }

    public async Task<byte[]?> DownloadStorageObjectAsync(string bucket, string path, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var encodedPath = string.Join("/", path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        var url = $"{baseUrl}/storage/v1/object/authenticated/{Uri.EscapeDataString(bucket)}/{encodedPath}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken ?? anon);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // Public object URL fallback
            var pub = $"{baseUrl}/storage/v1/object/public/{Uri.EscapeDataString(bucket)}/{encodedPath}";
            using var req2 = new HttpRequestMessage(HttpMethod.Get, pub);
            req2.Headers.TryAddWithoutValidation("apikey", anon);
            using var resp2 = await _http.SendAsync(req2, ct).ConfigureAwait(false);
            if (!resp2.IsSuccessStatusCode)
            {
                AppLog.Warn($"Storage download fail {(int)resp.StatusCode}/{(int)resp2.StatusCode}");
                return null;
            }

            return await resp2.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool LooksLikeStatus(string content)
    {
        var t = (content ?? string.Empty).TrimStart();
        return t.StartsWith('{')
               && t.Contains("\"hwt_status\"", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeScreenshot(string content)
    {
        var t = (content ?? string.Empty).TrimStart();
        if (!t.StartsWith('{'))
        {
            return false;
        }

        return t.Contains("\"hwt_screenshot\"", StringComparison.OrdinalIgnoreCase)
               || t.Contains("\"hwt_screenshot_repeat\"", StringComparison.OrdinalIgnoreCase)
               || (t.Contains("\"type\":\"file\"", StringComparison.OrdinalIgnoreCase)
                   && t.Contains("image/", StringComparison.OrdinalIgnoreCase));
    }

    private static TerminalLine ToLine(JsonElement row) => new()
    {
        Sender = GetStr(row, "sender_name"),
        Recipient = GetStr(row, "recipient_name"),
        Content = GetStr(row, "content"),
        CreatedAt = GetStr(row, "created_at"),
        MessageId = Guid.TryParse(GetStr(row, "id"), out var id) ? id : null,
    };

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null
            ? p.ToString()
            : string.Empty;

    private static string? TryJwtSub(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s[..n] + "…");
}
