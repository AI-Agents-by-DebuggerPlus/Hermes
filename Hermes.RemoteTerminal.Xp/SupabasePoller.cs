using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Hermes.RemoteTerminal.Xp;

/// <summary>REST poll of public.messages — view-only (no agent, no commands).</summary>
internal sealed class SupabasePoller : IDisposable
{
    private readonly AppSettings _settings;
    private string _accessToken;
    private Guid? _lastSeenId;
    private bool _baselineDone;
    private Thread _thread;
    private volatile bool _stop;

    public event Action<TerminalLine> LineReceived;
    public event Action<string> StatusChanged;

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

    public void Dispose() => Stop();

    private void Loop()
    {
        RaiseStatus("Supabase: connecting…");
        var tlsBlocked = false;
        while (!_stop)
        {
            try
            {
                if (!TlsBootstrap.Tls12Enabled && !CurlHttp.IsAvailable)
                {
                    if (!tlsBlocked)
                    {
                        tlsBlocked = true;
                        AppLog.Error("HTTPS blocked: no TLS 1.2 and no tools\\curl.exe");
                        RaiseStatus("HTTPS blocked — нужен tools\\curl.exe");
                    }
                }
                else
                {
                    EnsureToken();
                    PollOnce();
                    tlsBlocked = false;
                }
            }
            catch (Exception ex)
            {
                RaiseStatus("Poll error: " + Truncate(ex.Message, 120));
                AppLog.Warn("Poll: " + ex.Message);
                _accessToken = null;
                if (TlsBootstrap.LooksLikeTlsFailure(ex) && !CurlHttp.IsAvailable)
                    tlsBlocked = true;
            }

            var waitSec = tlsBlocked ? 60 : Math.Max(3, _settings.PollSeconds);
            var wait = waitSec * 1000;
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
        if (!string.IsNullOrWhiteSpace(_accessToken))
            return;

        var baseUrl = _settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = _settings.SupabaseAnonKey.Trim();

        var attempts = new[]
        {
            new { Url = baseUrl + "/auth/v1/signup", Body = "{\"data\":{}}" },
            new { Url = baseUrl + "/auth/v1/token?grant_type=anonymous", Body = "{}" },
        };

        foreach (var a in attempts)
        {
            try
            {
                var text = XpHttp.PostJson(a.Url, a.Body, anon, anon);
                var json = JObject.Parse(text);
                var token = json["access_token"] != null ? json["access_token"].ToString() : null;
                if (string.IsNullOrWhiteSpace(token)) continue;
                _accessToken = token;
                AppLog.Info("Supabase session OK (view-only)");
                RaiseStatus("Supabase: session OK");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Auth attempt fail: " + Truncate(ex.Message, 100));
            }
        }

        _accessToken = anon;
        RaiseStatus("Supabase: anon key (SELECT)");
        AppLog.Warn("Anonymous auth failed — using anon key for SELECT only");
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
            text = XpHttp.Get(url, anon, _accessToken);
        }
        catch (WebException wex)
        {
            var resp = wex.Response as HttpWebResponse;
            if (resp != null && (int)resp.StatusCode == 401)
            {
                _accessToken = null;
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
            RaiseStatus("Supabase: listening (view-only)…");
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
        if (!_settings.ShowAllRecipients)
        {
            if (!string.Equals(recipient, _settings.RecipientName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var sender = GetStr(row, "sender_name");
        var content = GetStr(row, "content");
        var created = GetStr(row, "created_at");
        var line = new TerminalLine
        {
            Sender = sender,
            Recipient = recipient,
            Content = content,
            CreatedAt = created,
        };
        var h = LineReceived;
        if (h != null) h(line);
    }

    private void RaiseStatus(string s)
    {
        var h = StatusChanged;
        if (h != null) h(s);
    }

    private static string GetStr(JObject o, string key)
    {
        var t = o[key];
        return t == null || t.Type == JTokenType.Null ? string.Empty : t.ToString();
    }

    private static string Truncate(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}

internal sealed class TerminalLine
{
    public string Sender { get; set; }
    public string Recipient { get; set; }
    public string Content { get; set; }
    public string CreatedAt { get; set; }
}
