using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Polls shared Supabase <c>messages</c> for english_lesson payloads (Hermes → this app).
/// REST only (Win7 / net48). Prefer anon JWT for SELECT; anonymous session optional.
/// </summary>
public sealed class SupabaseLessonPoller : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string? _accessToken;
    private Guid? _lastSeenId;
    private bool _baselineDone;

    public event Action<string, string>? LessonReceived;
    public event Action<string>? StatusChanged;

    public bool IsConfigured(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.SupabaseUrl) && !string.IsNullOrWhiteSpace(s.SupabaseAnonKey);

    public async Task EnsureSessionAsync(AppSettings settings, CancellationToken ct)
    {
        if (!IsConfigured(settings))
        {
            throw new InvalidOperationException("Укажите Supabase URL и anon key.");
        }

        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            return;
        }

        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();

        // 1) Anonymous sign-in (preferred when RLS requires authenticated)
        var attempts = new (string Url, string Body)[]
        {
            (baseUrl + "/auth/v1/signup", "{\"data\":{}}"),
            (baseUrl + "/auth/v1/token?grant_type=anonymous", "{}"),
        };

        foreach (var attempt in attempts)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, attempt.Url)
            {
                Content = new StringContent(attempt.Body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("apikey", anon);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + anon);

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                continue;
            }

            try
            {
                var json = JObject.Parse(text);
                var token = json["access_token"]?.ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    _accessToken = token;
                    RaiseStatus("Supabase: anonymous session OK");
                    return;
                }
            }
            catch
            {
                // try next
            }
        }

        // 2) Fallback: use anon key as bearer (works if SELECT allowed for anon role)
        _accessToken = anon;
        RaiseStatus("Supabase: using anon key (no anonymous session)");
    }

    public async Task PollOnceAsync(AppSettings settings, CancellationToken ct)
    {
        await EnsureSessionAsync(settings, ct).ConfigureAwait(false);

        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var url = baseUrl + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at&order=created_at.desc&limit=40";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("apikey", anon);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            if ((int)resp.StatusCode == 401)
            {
                _accessToken = null;
                RaiseStatus("Poll 401 — reconnect…");
                return;
            }

            RaiseStatus("Poll HTTP " + (int)resp.StatusCode + ": " + Truncate(text, 120));
            return;
        }

        var arr = JArray.Parse(text);
        var rows = new List<JObject>();
        foreach (var token in arr)
        {
            if (token is JObject o)
            {
                rows.Add(o);
            }
        }

        // First poll: only establish baseline (do not reload entire history as new lessons)
        if (!_baselineDone)
        {
            if (rows.Count > 0)
            {
                Guid.TryParse(rows[0]["id"]?.ToString(), out var top);
                _lastSeenId = top == Guid.Empty ? (Guid?)null : top;
            }

            _baselineDone = true;
            RaiseStatus("Supabase: listening for new lessons…");
            return;
        }

        // Newest first in response — walk until lastSeen
        var fresh = new List<JObject>();
        foreach (var row in rows)
        {
            Guid.TryParse(row["id"]?.ToString(), out var id);
            if (_lastSeenId.HasValue && id == _lastSeenId.Value)
            {
                break;
            }

            fresh.Add(row);
        }

        if (rows.Count > 0)
        {
            Guid.TryParse(rows[0]["id"]?.ToString(), out var newest);
            if (newest != Guid.Empty)
            {
                _lastSeenId = newest;
            }
        }

        // Process oldest → newest
        fresh.Reverse();
        foreach (var row in fresh)
        {
            var recipient = row["recipient_name"]?.ToString() ?? string.Empty;
            var content = row["content"]?.ToString() ?? string.Empty;

            if (!TryExtractLessonMarkdown(content, out var markdown, out var title))
            {
                continue;
            }

            var recipientOk = string.IsNullOrWhiteSpace(settings.RecipientName)
                              || string.Equals(recipient, settings.RecipientName, StringComparison.OrdinalIgnoreCase);

            if (!recipientOk)
            {
                continue;
            }

            RaiseStatus("Урок: " + (title ?? "english_lesson"));
            LessonReceived?.Invoke(markdown, title ?? "lesson");
        }
    }

    public static bool TryExtractLessonMarkdown(string content, out string markdown, out string? title)
    {
        markdown = string.Empty;
        title = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var t = content.Trim();
        if (t.StartsWith("[LOG:", StringComparison.Ordinal))
        {
            return false;
        }

        if (t.StartsWith("---", StringComparison.Ordinal)
            || t.StartsWith("## title", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("# ", StringComparison.Ordinal))
        {
            if (t.IndexOf("## words", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("## lyrics", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("## title", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                markdown = t;
                return true;
            }
        }

        if (!t.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var obj = JObject.Parse(t);
            var type = obj["type"]?.ToString();
            if (!string.Equals(type, "english_lesson", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "english_cards", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            title = obj["title"]?.ToString();
            markdown = obj["markdown"]?.ToString()
                       ?? obj["md"]?.ToString()
                       ?? obj["content"]?.ToString()
                       ?? string.Empty;
            return !string.IsNullOrWhiteSpace(markdown);
        }
        catch
        {
            return false;
        }
    }

    private void RaiseStatus(string s) => StatusChanged?.Invoke(s);

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s.Substring(0, n) + "…");

    public void Dispose() => _http.Dispose();
}
