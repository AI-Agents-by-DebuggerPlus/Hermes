using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.WpGallery;

public static class WpGalleryWebSocketSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<bool> TrySendImageAsync(
        WpGalleryImageFrame frame,
        WpGalleryWebSocketOptions options,
        CancellationToken cancellationToken = default)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(options.ConnectTimeout);
            await ws.ConnectAsync(new Uri(options.WebSocketUrl.Trim()), connectCts.Token).ConfigureAwait(false);

            var payload = new
            {
                type = "image",
                channel = WpGalleryEndpoints.EffectiveSender(options.Sender ?? options.Channel),
                filename = frame.Filename,
                mime = frame.MimeType,
                data = Convert.ToBase64String(frame.Data),
                meta = new
                {
                    width = frame.Width,
                    height = frame.Height,
                    timestamp = frame.CapturedAt.ToString("O"),
                },
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
