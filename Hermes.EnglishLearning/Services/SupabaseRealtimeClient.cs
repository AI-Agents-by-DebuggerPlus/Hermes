using System;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Supabase Realtime (Phoenix WebSocket) subscription to INSERT on public.messages.
/// Falls back to a slow poll only if the socket cannot connect.
/// </summary>
public sealed class SupabaseRealtimeClient : IDisposable
{
    private readonly SupabaseLessonPoller _restHelper = new();
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string? _accessToken;
    private AppSettings? _settings;
    private int _refCounter;
    private Task? _loop;

    public event Action<string, string>? LessonReceived;
    public event Action<string>? StatusChanged;

    public bool IsConfigured(AppSettings s) => _restHelper.IsConfigured(s);

    public async Task StartAsync(AppSettings settings, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        _settings = settings;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        await _restHelper.EnsureSessionAsync(settings, token).ConfigureAwait(false);
        // Reuse poller's token via a light probe — EnsureSession stores privately.
        // Call PollOnce once to establish baseline semantics, then open WS.
        // We duplicate auth here:
        _accessToken = await FetchAccessTokenAsync(settings, token).ConfigureAwait(false);

        _loop = Task.Run(() => RunLoopAsync(settings, token), token);
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

        if (_ws != null)
        {
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

            _ws.Dispose();
            _ws = null;
        }

        if (_loop != null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch
            {
            }

            _loop = null;
        }

        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunLoopAsync(AppSettings settings, CancellationToken ct)
    {
        var backoff = 2;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(settings, ct).ConfigureAwait(false);
                backoff = 2;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Realtime WS: " + ex.Message + " — reconnect in " + backoff + "s");
                RaiseStatus("Realtime: reconnect…");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(backoff), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = Math.Min(60, backoff * 2);
            }
        }
    }

    private async Task ConnectAndListenAsync(AppSettings settings, CancellationToken ct)
    {
        var baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
        var anon = settings.SupabaseAnonKey.Trim();
        var token = string.IsNullOrWhiteSpace(_accessToken) ? anon : _accessToken;
        string host;
        if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            host = "wss://" + baseUrl.Substring("https://".Length);
        }
        else if (baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            host = "ws://" + baseUrl.Substring("http://".Length);
        }
        else
        {
            host = "wss://" + baseUrl;
        }

        var wsUrl = host + "/realtime/v1/websocket?apikey=" + Uri.EscapeDataString(anon) + "&vsn=1.0.0";

        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("apikey", anon);
        if (!string.IsNullOrWhiteSpace(token))
        {
            _ws.Options.SetRequestHeader("Authorization", "Bearer " + token);
        }

        AppLog.Info("Realtime connecting " + host + "/realtime/v1/websocket");
        await _ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);
        RaiseStatus("Realtime: connected");
        AppLog.Info("Realtime connected");

        // Join postgres_changes channel
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

        // Heartbeat
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
                    throw new InvalidOperationException("Realtime closed by server");
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            HandleMessage(sb.ToString(), settings);
        }
    }

    private void HandleMessage(string raw, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            var msg = JObject.Parse(raw);
            var ev = msg["event"]?.ToString() ?? string.Empty;
            if (string.Equals(ev, "phx_reply", StringComparison.OrdinalIgnoreCase))
            {
                var status = msg["payload"]?["status"]?.ToString();
                AppLog.Info("Realtime phx_reply status=" + status);
                if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    RaiseStatus("Realtime: subscribed to messages INSERT");
                }

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

            // Some payloads nest under payload.data.record
            if (record == null && payload?["data"] is JObject d2)
            {
                record = d2["record"] as JObject ?? d2;
            }

            if (record == null)
            {
                return;
            }

            var recipient = record["recipient_name"]?.ToString() ?? string.Empty;
            var content = record["content"]?.ToString() ?? string.Empty;
            if (!SupabaseLessonPoller.TryExtractLessonMarkdown(content, out var markdown, out var title))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.RecipientName)
                && !string.Equals(recipient, settings.RecipientName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
            .ConfigureAwait(false);
    }

    private string NextRef() => Interlocked.Increment(ref _refCounter).ToString(CultureInfo.InvariantCulture);

    private async Task<string> FetchAccessTokenAsync(AppSettings settings, CancellationToken ct)
    {
        // Prefer anonymous session; fall back to anon key.
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
                {
                    continue;
                }

                var json = JObject.Parse(text);
                var token = json["access_token"]?.ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token!;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Auth for Realtime: " + ex.Message);
        }

        return settings.SupabaseAnonKey.Trim();
    }

    private void RaiseStatus(string s) => StatusChanged?.Invoke(s);

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _restHelper.Dispose();
    }
}
