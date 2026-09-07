using System;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Supabase Realtime WebSocket + REST poll fallback (when WSS cannot connect).
/// </summary>
public sealed class SupabaseRealtimeClient : IDisposable
{
    private readonly SupabaseLessonPoller _poller = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string? _accessToken;
    private int _refCounter;
    private Task? _wsLoop;
    private Task? _pollLoop;
    private int _wsFailCount;
    private volatile bool _wsConnected;

    public event Action<string, string>? LessonReceived;
    public event Action<EnglishNavCommand>? NavReceived;
    public event Action<string>? StatusChanged;

    public bool IsConfigured(AppSettings s) => _poller.IsConfigured(s);

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;
        _wsFailCount = 0;
        _wsConnected = false;

        _poller.LessonReceived += ForwardLesson;
        _poller.NavReceived += ForwardNav;
        _poller.StatusChanged += RaiseStatus;

        await _poller.EnsureSessionAsync(settings, token).ConfigureAwait(false);
        _accessToken = await FetchAccessTokenAsync(settings, token).ConfigureAwait(false);

        // REST poll always runs — primary path when WSS is blocked (common on some Win10 nets).
        _pollLoop = Task.Run(() => PollLoopAsync(settings, token), token);
        _wsLoop = Task.Run(() => WsLoopAsync(settings, token), token);
        RaiseStatus("Supabase: poll + Realtime starting…");
    }

    public async Task StopAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        await CloseWsAsync().ConfigureAwait(false);

        if (_pollLoop != null)
        {
            try { await _pollLoop.ConfigureAwait(false); } catch { /* ignore */ }
            _pollLoop = null;
        }

        if (_wsLoop != null)
        {
            try { await _wsLoop.ConfigureAwait(false); } catch { /* ignore */ }
            _wsLoop = null;
        }

        _poller.LessonReceived -= ForwardLesson;
        _poller.NavReceived -= ForwardNav;
        _poller.StatusChanged -= RaiseStatus;

        _cts?.Dispose();
        _cts = null;
        _wsConnected = false;
    }

    private async Task PollLoopAsync(AppSettings settings, CancellationToken ct)
    {
        // First poll establishes baseline quickly.
        var delaySec = Math.Max(3, settings.PollSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _poller.PollOnceAsync(settings, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn("REST poll: " + ex.Message);
                RaiseStatus("Poll: " + Truncate(ex.Message, 80));
            }

            // While WS is healthy, poll slower; when WS is down, use configured interval.
            var wait = _wsConnected ? Math.Max(delaySec, 20) : delaySec;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(wait), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task WsLoopAsync(AppSettings settings, CancellationToken ct)
    {
        var backoff = 2;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(settings, ct).ConfigureAwait(false);
                backoff = 2;
                _wsFailCount = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _wsConnected = false;
                _wsFailCount++;
                AppLog.Warn("Realtime WS: " + ex.Message + " — reconnect in " + backoff + "s (poll active)");
                RaiseStatus(_wsFailCount <= 2
                    ? "Realtime: reconnect… (REST poll active)"
                    : "Realtime offline — REST poll every " + Math.Max(3, settings.PollSeconds) + "s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // After a few failures, stop spamming every minute — poll already delivers lessons.
                if (_wsFailCount >= 5)
                    backoff = 900; // 15 min
                else
                    backoff = Math.Min(60, backoff * 2);

                // Refresh token occasionally after failures.
                if (_wsFailCount % 5 == 0)
                {
                    try
                    {
                        _accessToken = await FetchAccessTokenAsync(settings, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }
    }

    private async Task ConnectAndListenAsync(AppSettings settings, CancellationToken ct)
    {
        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var token = string.IsNullOrWhiteSpace(_accessToken) ? anon : _accessToken!;

        // Build wss URL from https base (hostname — ClientWebSocket cannot override Host for raw IP).
        string hostPath;
        string originalHost;
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            originalHost = baseUrl.Substring("https://".Length).Split('/')[0];
            hostPath = "wss://" + baseUrl.Substring("https://".Length);
        }
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            originalHost = baseUrl.Substring("http://".Length).Split('/')[0];
            hostPath = "ws://" + baseUrl.Substring("http://".Length);
        }
        else
        {
            originalHost = baseUrl.Split('/')[0];
            hostPath = "wss://" + baseUrl;
        }

        var wsUrl = hostPath.TrimEnd('/') + "/realtime/v1/websocket"
                    + "?apikey=" + Uri.EscapeDataString(anon)
                    + "&vsn=1.0.0"
                    + "&access_token=" + Uri.EscapeDataString(token);

        await CloseWsAsync().ConfigureAwait(false);
        _ws = new ClientWebSocket();
        try
        {
            _ws.Options.SetRequestHeader("apikey", anon);
            _ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Realtime headers: " + ex.Message);
        }

        AppLog.Info("Realtime connecting " + originalHost + "/realtime/v1/websocket");
        await _ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
        _wsConnected = true;
        RaiseStatus("Realtime: connected");
        AppLog.Info("Realtime connected");

        var joinRef = NextRef();
        var topic = "realtime:english-learning-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var join = new JObject
        {
            ["topic"] = topic,
            ["event"] = "phx_join",
            ["payload"] = new JObject
            {
                ["config"] = new JObject
                {
                    ["broadcast"] = new JObject { ["self"] = false },
                    ["presence"] = new JObject { ["key"] = "" },
                    ["postgres_changes"] = new JArray
                    {
                        new JObject
                        {
                            ["event"] = "INSERT",
                            ["schema"] = "public",
                            ["table"] = "messages",
                        },
                    },
                },
                ["access_token"] = token,
            },
            ["ref"] = joinRef,
        };

        await SendJsonAsync(join, ct).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    await Task.Delay(25000, ct).ConfigureAwait(false);
                    var hb = new JObject
                    {
                        ["topic"] = "phoenix",
                        ["event"] = "heartbeat",
                        ["payload"] = new JObject(),
                        ["ref"] = NextRef(),
                    };
                    await SendJsonAsync(hb, ct).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }
        }, ct);

        var buffer = new byte[64 * 1024];
        var sb = new StringBuilder();
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _wsConnected = false;
                    throw new InvalidOperationException("Realtime closed by server");
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            HandleMessage(sb.ToString(), settings);
        }

        _wsConnected = false;
    }

    private void HandleMessage(string raw, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        try
        {
            var msg = JObject.Parse(raw);
            var ev = msg["event"]?.ToString() ?? string.Empty;
            if (string.Equals(ev, "phx_reply", StringComparison.OrdinalIgnoreCase))
            {
                var status = msg["payload"]?["status"]?.ToString();
                AppLog.Info("Realtime phx_reply status=" + status);
                if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                    RaiseStatus("Realtime: subscribed (REST poll backup on)");
                return;
            }

            if (!string.Equals(ev, "postgres_changes", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ev, "INSERT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var payload = msg["payload"] as JObject;
            var data = payload?["data"] as JObject ?? payload;
            var record = data?["record"] as JObject
                         ?? payload?["record"] as JObject
                         ?? data;

            if (record == null && payload?["data"] is JObject d2)
                record = d2["record"] as JObject ?? d2;

            if (record == null)
                return;

            Guid.TryParse(record["id"]?.ToString(), out var id);
            if (id != Guid.Empty && !SeenMessageIds.TryMark(id))
                return;

            var recipient = record["recipient_name"]?.ToString() ?? string.Empty;
            var content = record["content"]?.ToString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(settings.RecipientName)
                && !string.Equals(recipient, settings.RecipientName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (EnglishNavParser.TryParse(content, out var nav) && nav != EnglishNavCommand.None)
            {
                AppLog.Info("Realtime nav: " + nav);
                RaiseStatus("Nav (WS): " + nav);
                NavReceived?.Invoke(nav);
                return;
            }

            if (!SupabaseLessonPoller.TryExtractLessonMarkdown(content, out var markdown, out var title))
                return;

            AppLog.Info("Realtime lesson received: " + (title ?? "english_lesson"));
            RaiseStatus("Урок (WS): " + (title ?? "english_lesson"));
            LessonReceived?.Invoke(markdown, title ?? "lesson");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Realtime parse: " + ex.Message);
        }
    }

    private async Task SendJsonAsync(JObject obj, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);
    }

    private async Task CloseWsAsync()
    {
        _wsConnected = false;
        if (_ws == null) return;
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
        }

        try { _ws.Dispose(); } catch { /* ignore */ }
        _ws = null;
    }

    private string NextRef() => Interlocked.Increment(ref _refCounter).ToString(CultureInfo.InvariantCulture);

    private async Task<string> FetchAccessTokenAsync(AppSettings settings, CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
            var anon = settings.SupabaseAnonKey.Trim();
            foreach (var path in new[]
                     {
                         "/auth/v1/signup",
                         "/auth/v1/token?grant_type=anonymous",
                     })
            {
                var body = path.IndexOf("signup", StringComparison.OrdinalIgnoreCase) >= 0 ? "{\"data\":{}}" : "{}";
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, baseUrl + path)
                {
                    Content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("apikey", anon);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + anon);
                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var json = JObject.Parse(text);
                var tok = json["access_token"]?.ToString();
                if (!string.IsNullOrWhiteSpace(tok))
                    return tok!;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Auth for Realtime: " + ex.Message);
        }

        return settings.SupabaseAnonKey.Trim();
    }

    private void ForwardLesson(string md, string title) => LessonReceived?.Invoke(md, title);
    private void ForwardNav(EnglishNavCommand nav) => NavReceived?.Invoke(nav);
    private void RaiseStatus(string s) => StatusChanged?.Invoke(s);

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= n ? s : s.Substring(0, n) + "…");

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _poller.Dispose();
    }
}
