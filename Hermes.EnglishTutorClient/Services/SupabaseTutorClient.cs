using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishTutorClient.Services;

public sealed class SupabaseTutorClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _accessToken;
    private string? _userId;
    private Guid? _lastSeenId;
    private bool _baselineDone;

    public bool IsConnected { get; private set; }
    public string StatusText { get; private set; } = "Supabase: —";

    public event Action<string>? StatusChanged;
    public event Action<string, string>? MessageReceived; // sender, content

    public bool IsConfigured(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.SupabaseUrl) && !string.IsNullOrWhiteSpace(s.SupabaseAnonKey);

    public async Task<bool> ConnectAsync(AppSettings settings, CancellationToken ct)
    {
        if (!IsConfigured(settings))
        {
            IsConnected = false;
            RaiseStatus("Supabase: не настроен");
            return false;
        }

        try
        {
            await EnsureSessionAsync(settings, ct).ConfigureAwait(false);
            // Health: lightweight SELECT
            var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
            var anon = settings.SupabaseAnonKey.Trim();
            var url = baseUrl + "/rest/v1/messages?select=id&limit=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("apikey", anon);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                IsConnected = false;
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                RaiseStatus("Supabase: ошибка " + (int)resp.StatusCode);
                AppLog.Warn("Supabase health fail: " + Truncate(body, 160));
                return false;
            }

            IsConnected = true;
            _baselineDone = false;
            _lastSeenId = null;
            RaiseStatus("Supabase: подключено");
            return true;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            RaiseStatus("Supabase: нет связи");
            AppLog.Warn("Supabase connect: " + ex.Message);
            return false;
        }
    }

    public void Disconnect()
    {
        IsConnected = false;
        _accessToken = null;
        _userId = null;
        RaiseStatus("Supabase: отключено");
    }

    public async Task EnsureSessionAsync(AppSettings settings, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && !string.IsNullOrWhiteSpace(_userId))
            return;

        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var attempts = new (string Url, string Body)[]
        {
            (baseUrl + "/auth/v1/signup", "{\"data\":{}}"),
            (baseUrl + "/auth/v1/token?grant_type=anonymous", "{}"),
        };

        foreach (var attempt in attempts)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, attempt.Url)
            {
                Content = new StringContent(attempt.Body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("apikey", anon);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + anon);
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) continue;
            try
            {
                var json = JObject.Parse(text);
                var token = json["access_token"]?.ToString();
                var uid = json["user"]?["id"]?.ToString()
                          ?? json["id"]?.ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _accessToken = token;
                    if (!string.IsNullOrWhiteSpace(uid))
                        _userId = uid;
                    // Decode uid from JWT if missing
                    if (string.IsNullOrWhiteSpace(_userId))
                        _userId = TryReadJwtSub(token!);
                    AppLog.Info("Supabase anonymous session OK uid=" + (_userId ?? "?"));
                    return;
                }
            }
            catch { /* next */ }
        }

        _accessToken = anon;
        _userId = Guid.NewGuid().ToString();
        AppLog.Warn("Supabase: using anon key fallback (INSERT may fail RLS)");
    }

    public async Task SendAsync(AppSettings settings, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        await EnsureSessionAsync(settings, ct).ConfigureAwait(false);

        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var payload = new
        {
            sender_id = _userId,
            sender_name = settings.RecipientName,
            recipient_name = settings.HermesRecipientName,
            content = content,
            created_at = DateTimeOffset.UtcNow.ToString("o"),
        };
        var json = JsonConvert.SerializeObject(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/rest/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Add("Prefer", "return=minimal");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 401)
            {
                _accessToken = null;
                _userId = null;
            }

            throw new InvalidOperationException("INSERT HTTP " + (int)resp.StatusCode + ": " + Truncate(body, 200));
        }

        AppLog.Info("Supabase sent chars=" + content.Length);
    }

    public async Task PollOnceAsync(AppSettings settings, CancellationToken ct)
    {
        if (!IsConnected) return;
        await EnsureSessionAsync(settings, ct).ConfigureAwait(false);

        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var url = baseUrl + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at&order=created_at.desc&limit=40";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 401)
            {
                _accessToken = null;
                IsConnected = false;
                RaiseStatus("Supabase: сессия истекла");
            }
            return;
        }

        var arr = JArray.Parse(text);
        var rows = new List<JObject>();
        foreach (var t in arr)
        {
            if (t is JObject o) rows.Add(o);
        }

        if (!_baselineDone)
        {
            if (rows.Count > 0 && Guid.TryParse(rows[0]["id"]?.ToString(), out var top) && top != Guid.Empty)
                _lastSeenId = top;
            _baselineDone = true;
            RaiseStatus("Supabase: слушаю Hermes…");
            return;
        }

        var fresh = new List<JObject>();
        foreach (var row in rows)
        {
            Guid.TryParse(row["id"]?.ToString(), out var id);
            if (_lastSeenId.HasValue && id == _lastSeenId.Value) break;
            fresh.Add(row);
        }

        if (rows.Count > 0 && Guid.TryParse(rows[0]["id"]?.ToString(), out var newest) && newest != Guid.Empty)
            _lastSeenId = newest;

        fresh.Reverse();
        var myName = settings.RecipientName;
        var hermesFilter = settings.SenderNameFilter;
        foreach (var row in fresh)
        {
            var recipient = (row["recipient_name"]?.ToString() ?? string.Empty).Trim();
            var sender = (row["sender_name"]?.ToString() ?? string.Empty).Trim();
            if (!string.Equals(recipient, myName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.IsNullOrWhiteSpace(hermesFilter)
                && !string.Equals(sender, hermesFilter, StringComparison.OrdinalIgnoreCase)
                && !sender.StartsWith("Hermes", StringComparison.OrdinalIgnoreCase))
                continue;

            var content = row["content"]?.ToString() ?? string.Empty;
            if (content.Length == 0) continue;
            MessageReceived?.Invoke(sender, content);
        }
    }

    private void RaiseStatus(string s)
    {
        StatusText = s;
        StatusChanged?.Invoke(s);
    }

    private static string? TryReadJwtSub(string jwt)
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
            return JObject.Parse(json)["sub"]?.ToString();
        }
        catch { return null; }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "…";

    public void Dispose() => _http.Dispose();
}
