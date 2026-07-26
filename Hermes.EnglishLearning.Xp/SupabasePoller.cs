using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Xp;

/// <summary>REST poll of public.messages (XP-safe; no Realtime WebSocket).</summary>
internal sealed class SupabasePoller : IDisposable
{
    private readonly AppSettings _settings;
    private string _accessToken;
    private string _userId;
    private Guid? _lastSeenId;
    private bool _baselineDone;
    private Thread _thread;
    private volatile bool _stop;

    public event Action<string, string> LessonReceived;
    public event Action<NavCommand> NavReceived;
    public event Action<string> StatusChanged;

    private string _lastPublishedContent = string.Empty;
    private int _lastPublishedIndex = -1;

    public SupabasePoller(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException("settings");
    }

    public bool IsConfigured
    {
        get
        {
            return !string.IsNullOrWhiteSpace(_settings.SupabaseUrl)
                   && !string.IsNullOrWhiteSpace(_settings.SupabaseAnonKey);
        }
    }

    public void Start()
    {
        if (!IsConfigured || _thread != null) return;
        _stop = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SupabasePoll" };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        try
        {
            if (_thread != null && !_thread.Join(2000))
            {
                // ignore
            }
        }
        catch
        {
        }

        _thread = null;
    }

    private void Loop()
    {
        RaiseStatus("Supabase: connecting…");
        while (!_stop)
        {
            try
            {
                EnsureToken();
                PollOnce();
            }
            catch (Exception ex)
            {
                RaiseStatus("Poll error: " + Truncate(ex.Message, 120));
                AppLog.Warn("Poll: " + ex.Message);
                _accessToken = null;
                _userId = null;
            }

            var wait = Math.Max(3, _settings.PollSeconds) * 1000;
            var stepped = 0;
            while (!_stop && stepped < wait)
            {
                Thread.Sleep(200);
                stepped += 200;
            }
        }
    }

    private void EnsureToken()
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) && !string.IsNullOrWhiteSpace(_userId))
            return;

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        _accessToken = null;
        _userId = null;

        // Anonymous auth required for INSERT (sender_id = auth.uid()).
        var attempts = new[]
        {
            new { Url = baseUrl + "/auth/v1/signup", Body = "{\"data\":{}}" },
            new { Url = baseUrl + "/auth/v1/token?grant_type=anonymous", Body = "{}" },
        };

        foreach (var a in attempts)
        {
            try
            {
                var text = HttpPost(a.Url, a.Body, anon, anon);
                var json = JObject.Parse(text);
                var token = json["access_token"] != null ? json["access_token"].ToString() : null;
                if (string.IsNullOrWhiteSpace(token)) continue;

                var uid = json["user"] != null && json["user"]["id"] != null
                    ? json["user"]["id"].ToString()
                    : null;
                if (string.IsNullOrWhiteSpace(uid))
                    uid = TryGetJwtSub(token);

                if (string.IsNullOrWhiteSpace(uid))
                {
                    AppLog.Warn("Auth OK but no user id — try next");
                    continue;
                }

                _accessToken = token;
                _userId = uid;
                AppLog.Info("Supabase anonymous session uid=" + uid);
                RaiseStatus("Supabase: session OK");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Auth attempt fail: " + Truncate(ex.Message, 100));
            }
        }

        // SELECT-only fallback (no INSERT)
        _accessToken = anon;
        _userId = null;
        RaiseStatus("Supabase: anon key (TTS insert disabled)");
        AppLog.Warn("Anonymous auth failed — page TTS INSERT will not work until Anonymous provider is ON");
    }

    private static string TryGetJwtSub(string jwt)
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
            var obj = JObject.Parse(json);
            return obj["sub"] != null ? obj["sub"].ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private void PollOnce()
    {
        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();
        var url = baseUrl
                  + "/rest/v1/messages?select=id,sender_name,recipient_name,content,created_at&order=created_at.desc&limit=40";
        string text;
        try
        {
            text = HttpGet(url, anon, _accessToken);
        }
        catch (WebException wex)
        {
            var resp = wex.Response as HttpWebResponse;
            if (resp != null && (int)resp.StatusCode == 401)
            {
                _accessToken = null;
                _userId = null;
                RaiseStatus("Poll 401 — reconnect…");
                return;
            }

            throw;
        }

        var arr = JArray.Parse(text);
        var rows = new List<JObject>();
        foreach (var token in arr)
        {
            var o = token as JObject;
            if (o != null) rows.Add(o);
        }

        if (!_baselineDone)
        {
            if (rows.Count > 0)
            {
                Guid id;
                if (Guid.TryParse(GetStr(rows[0], "id"), out id) && id != Guid.Empty)
                    _lastSeenId = id;
            }

            _baselineDone = true;
            RaiseStatus("Supabase: listening…");
            return;
        }

        var fresh = new List<JObject>();
        foreach (var row in rows)
        {
            Guid id;
            Guid.TryParse(GetStr(row, "id"), out id);
            if (_lastSeenId.HasValue && id == _lastSeenId.Value) break;
            fresh.Add(row);
        }

        if (rows.Count > 0)
        {
            Guid newest;
            if (Guid.TryParse(GetStr(rows[0], "id"), out newest) && newest != Guid.Empty)
                _lastSeenId = newest;
        }

        fresh.Reverse();
        foreach (var row in fresh)
            HandleRow(row);
    }

    private void HandleRow(JObject row)
    {
        var recipient = GetStr(row, "recipient_name");
        var content = GetStr(row, "content");
        var recipientOk = string.IsNullOrWhiteSpace(_settings.RecipientName)
                          || string.Equals(recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase);
        if (!recipientOk) return;

        NavCommand nav;
        if (MessageParser.TryNav(content, out nav))
        {
            AppLog.Info("Nav from Supabase: " + nav);
            RaiseStatus("Nav: " + nav);
            var h = NavReceived;
            if (h != null) h(nav);
            return;
        }

        string md, title;
        if (!MessageParser.TryExtractLesson(content, out md, out title)) return;

        RaiseStatus("Lesson: " + (title ?? "english_lesson"));
        var lh = LessonReceived;
        if (lh != null) lh(md, title ?? "lesson");
    }

    /// <summary>
    /// Publish current lesson page as bilingual TTS for AndroidChat (background).
    /// </summary>
    public void PublishPageTtsAsync(LessonScreen screen, int screenIndex, int screenCount, Action<string> onUiStatus)
    {
        if (!IsConfigured || screen == null) return;
        var content = PageTtsFormatter.FormatScreen(screen);
        if (string.IsNullOrWhiteSpace(content))
        {
            AppLog.Warn("TTS publish skipped — empty page");
            return;
        }

        if (screenIndex == _lastPublishedIndex
            && string.Equals(content, _lastPublishedContent, StringComparison.Ordinal))
        {
            AppLog.Info("TTS publish skipped — same page #" + (screenIndex + 1));
            return;
        }

        _lastPublishedIndex = screenIndex;
        _lastPublishedContent = content;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                EnsureToken();
                if (string.IsNullOrWhiteSpace(_userId) || string.Equals(_accessToken, _settings.SupabaseAnonKey.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Нужна anonymous-сессия Supabase (Authentication → Anonymous = ON). sender_id обязателен.");
                }

                var recipient = string.IsNullOrWhiteSpace(_settings.TtsRecipientName)
                    ? "AndroidChat"
                    : _settings.TtsRecipientName.Trim();
                var sender = string.IsNullOrWhiteSpace(_settings.TtsSenderName)
                    ? "EnglishLearning"
                    : _settings.TtsSenderName.Trim();

                var payload = new JObject
                {
                    ["sender_id"] = _userId,
                    ["sender_name"] = sender,
                    ["recipient_name"] = recipient,
                    ["content"] = content,
                    ["created_at"] = DateTime.UtcNow.ToString("o"),
                };
                var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
                var anon = _settings.SupabaseAnonKey.Trim();
                var url = baseUrl + "/rest/v1/messages";
                try
                {
                    HttpPostJson(url, payload.ToString(Newtonsoft.Json.Formatting.None), anon, _accessToken, preferMinimal: true);
                }
                catch (WebException wex)
                {
                    var detail = ReadWebException(wex);
                    throw new InvalidOperationException(detail, wex);
                }

                var preview = Truncate(content.Replace('\n', ' '), 80);
                AppLog.Info("TTS → " + recipient + " page=" + (screenIndex + 1) + "/" + screenCount
                    + " chars=" + content.Length + " " + preview);
                if (onUiStatus != null)
                    onUiStatus("TTS → " + recipient + " экран " + (screenIndex + 1) + "/" + screenCount);
            }
            catch (Exception ex)
            {
                AppLog.Error("TTS publish FAIL: " + ex.Message);
                if (onUiStatus != null)
                    onUiStatus("TTS publish FAIL: " + Truncate(ex.Message, 80));
                _lastPublishedIndex = -1;
                _lastPublishedContent = string.Empty;
            }
        });
    }

    private static string ReadWebException(WebException wex)
    {
        var msg = wex.Message;
        try
        {
            var r = wex.Response as HttpWebResponse;
            if (r == null) return msg;
            msg = "HTTP " + (int)r.StatusCode + ": " + wex.Message;
            using (var stream = r.GetResponseStream())
            {
                if (stream == null) return msg;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var body = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(body))
                        msg += " body=" + Truncate(body, 200);
                }
            }
        }
        catch { /* ignore */ }
        return msg;
    }

    private static string GetStr(JObject o, string key)
    {
        var t = o[key];
        return t == null ? string.Empty : (t.ToString() ?? string.Empty);
    }

    private static string HttpGet(string url, string apikey, string bearer)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "GET";
        req.Timeout = 25000;
        req.ReadWriteTimeout = 25000;
        req.Accept = "application/json";
        req.Headers["apikey"] = apikey;
        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var stream = resp.GetResponseStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            return reader.ReadToEnd();
    }

    private static string HttpPost(string url, string body, string apikey, string bearer) =>
        HttpPostJson(url, body, apikey, bearer, preferMinimal: false);

    private static string HttpPostJson(string url, string body, string apikey, string bearer, bool preferMinimal)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.Timeout = 25000;
        req.ContentType = "application/json";
        req.ContentLength = bytes.Length;
        req.Headers["apikey"] = apikey;
        req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
        if (preferMinimal)
            req.Headers["Prefer"] = "return=minimal";
        using (var s = req.GetRequestStream())
            s.Write(bytes, 0, bytes.Length);
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var stream = resp.GetResponseStream())
        {
            if (stream == null) return string.Empty;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }

    private void RaiseStatus(string s)
    {
        var h = StatusChanged;
        if (h != null) h(s);
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }

    public void Dispose() => Stop();
}
