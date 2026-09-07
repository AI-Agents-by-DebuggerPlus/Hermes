using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Supabase Realtime via Phoenix WebSocket (anon apikey), same approach as WpfChat / AndroidChat.
/// Avoids supabase-csharp WebSocket 403 handshake failures.
/// </summary>
public sealed class SupabaseRealtimeWebSocket : IAsyncDisposable
{
    private const string Topic = "realtime:public:messages";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan MinReconnect = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnect = TimeSpan.FromSeconds(30);

    private readonly LogService _log;
    private readonly object _gate = new();

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private int _refSeq;
    private string? _url;

    public SupabaseRealtimeWebSocket(LogService log)
    {
        _log = log;
    }

    public event Action<SupabaseMessageRow>? MessageInserted;

    public event Action<string>? StatusChanged;

    public bool IsConnected { get; private set; }

    public void Start(string supabaseUrl, string anonKey)
    {
        var url = BuildWebsocketUrl(supabaseUrl, anonKey);
        if (url is null)
        {
            StatusChanged?.Invoke("WebSocket: нет URL");
            return;
        }

        Stop();
        _url = url;
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
        StatusChanged?.Invoke("WebSocket: подключение…");
    }

    public void Stop()
    {
        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        _url = null;
        IsConnected = false;
        StatusChanged?.Invoke("WebSocket: отключено");
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        await Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var backoff = MinReconnect;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectUntilClosedAsync(ct);
                backoff = MinReconnect;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                // Server idle close is common; reconnect loop handles it — don't alarm as ERROR.
                var msg = ex.Message ?? string.Empty;
                var expectedClose = msg.Contains("without completing the close handshake", StringComparison.OrdinalIgnoreCase)
                                    || msg.Contains("remote party closed", StringComparison.OrdinalIgnoreCase)
                                    || ex is WebSocketException;
                if (expectedClose)
                    _log.LogWarn($"[supabase] WebSocket realtime closed: {msg}");
                else
                    _log.LogError($"[supabase] WebSocket realtime error: {msg}");
                StatusChanged?.Invoke($"WebSocket: сбой — {msg}");
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            StatusChanged?.Invoke("WebSocket: переподключение…");
            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromMilliseconds(
                Math.Min(backoff.TotalMilliseconds * 2, MaxReconnect.TotalMilliseconds));
        }
    }

    private async Task ConnectUntilClosedAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_url))
        {
            throw new InvalidOperationException("Realtime WebSocket URL is empty.");
        }

        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(_url), ct);
        IsConnected = true;
        StatusChanged?.Invoke("WebSocket: активен");
        _log.LogInfo("[supabase] Realtime WebSocket connected (Phoenix/anon).");

        await SendTextAsync(socket, BuildJoinJson(), ct);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(() => HeartbeatLoopAsync(socket, heartbeatCts.Token), heartbeatCts.Token);

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var text = await ReceiveTextAsync(socket, ct);
                if (text is null)
                {
                    break;
                }

                HandleMessage(text);
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeat;
            }
            catch
            {
                // ignored
            }

            IsConnected = false;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, ct);
                await SendTextAsync(socket, BuildHeartbeatJson(), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarn($"[supabase] WebSocket heartbeat error: {ex.Message}");
                break;
            }
        }
    }

    private void HandleMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                HandleArrayFrame(root);
                return;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
            switch (evt)
            {
                case "phx_reply":
                    if (root.TryGetProperty("payload", out var payload)
                        && payload.TryGetProperty("status", out var status)
                        && status.GetString() == "error")
                    {
                        _log.LogError($"[supabase] Realtime join error: {payload}");
                        StatusChanged?.Invoke("WebSocket: join error");
                    }
                    else
                    {
                        StatusChanged?.Invoke("WebSocket: канал messages OK");
                        _log.LogInfo("[supabase] WebSocket: канал messages OK");
                    }

                    break;
                case "postgres_changes":
                    if (root.TryGetProperty("payload", out var changePayload))
                    {
                        DispatchPostgresPayload(changePayload);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[supabase] Realtime parse error: {ex.Message}");
        }
    }

    private void HandleArrayFrame(JsonElement arr)
    {
        // Phoenix V1 array: [join_ref, ref, topic, event, payload]
        if (arr.GetArrayLength() < 5)
        {
            return;
        }

        var evt = arr[3].GetString();
        if (evt != "postgres_changes")
        {
            return;
        }

        DispatchPostgresPayload(arr[4]);
    }

    private void DispatchPostgresPayload(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data))
        {
            return;
        }

        if (!string.Equals(GetString(data, "table"), "messages", StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(GetString(data, "type"), "INSERT", StringComparison.Ordinal))
        {
            return;
        }

        if (!data.TryGetProperty("record", out var record) || record.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var idText = GetString(record, "id");
        if (string.IsNullOrWhiteSpace(idText) || !Guid.TryParse(idText, out var id))
        {
            return;
        }

        var content = GetString(record, "content") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var createdAt = DateTime.UtcNow;
        var createdRaw = GetString(record, "created_at");
        if (!string.IsNullOrWhiteSpace(createdRaw)
            && DateTimeOffset.TryParse(createdRaw, out var parsed))
        {
            createdAt = parsed.UtcDateTime;
        }

        var row = new SupabaseMessageRow
        {
            Id = id,
            SenderId = GetString(record, "sender_id") ?? string.Empty,
            SenderName = GetString(record, "sender_name") ?? string.Empty,
            RecipientName = GetString(record, "recipient_name") ?? string.Empty,
            Content = content,
            CreatedAt = createdAt,
        };

        MessageInserted?.Invoke(row);
    }

    private string NextRef()
    {
        lock (_gate)
        {
            return Interlocked.Increment(ref _refSeq).ToString();
        }
    }

    private string BuildJoinJson()
    {
        var joinRef = NextRef();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("topic", Topic);
            writer.WriteString("event", "phx_join");
            writer.WritePropertyName("payload");
            writer.WriteStartObject();
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WritePropertyName("broadcast");
            writer.WriteStartObject();
            writer.WriteBoolean("ack", false);
            writer.WriteBoolean("self", false);
            writer.WriteEndObject();
            writer.WritePropertyName("presence");
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", false);
            writer.WriteEndObject();
            writer.WritePropertyName("postgres_changes");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("event", "INSERT");
            writer.WriteString("schema", "public");
            writer.WriteString("table", "messages");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("private", false);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteString("ref", joinRef);
            writer.WriteString("join_ref", joinRef);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string BuildHeartbeatJson()
    {
        var r = NextRef();
        return $"{{\"topic\":\"phoenix\",\"event\":\"heartbeat\",\"payload\":{{}},\"ref\":\"{r}\"}}";
    }

    private static string? BuildWebsocketUrl(string supabaseUrl, string anonKey)
    {
        var baseUrl = supabaseUrl.Trim().TrimEnd('/');
        var key = anonKey.Trim();
        if (baseUrl.Length == 0 || key.Length == 0)
        {
            return null;
        }

        var wsBase = baseUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
        return $"{wsBase}/realtime/v1/websocket?apikey={Uri.EscapeDataString(key)}&vsn=1.0.0";
    }

    private static async Task SendTextAsync(ClientWebSocket socket, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => prop.ToString(),
        };
    }
}
