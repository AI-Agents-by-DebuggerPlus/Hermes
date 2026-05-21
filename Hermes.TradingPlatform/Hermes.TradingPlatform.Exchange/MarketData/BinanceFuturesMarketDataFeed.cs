using System.Net.WebSockets;
using System.Text;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Exchange.MarketData;

/// <summary>Binance USDT-M Futures public ticker WebSocket (Phase 4).</summary>
public sealed class BinanceFuturesMarketDataFeed : IMarketDataFeed
{
    private const string CombinedStreamBase = "wss://fstream.binance.com/stream?streams=";

    private readonly IEventBus _bus;
    private readonly IReadOnlyList<string> _symbols;
    private CancellationTokenSource _cts = new();
    private Task? _runTask;

    public BinanceFuturesMarketDataFeed(IEventBus bus, IEnumerable<string> symbols)
    {
        _bus = bus;
        _symbols = symbols
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string Name => "Binance Futures";
    public MarketFeedStatus Status { get; private set; } = MarketFeedStatus.Stopped;
    public event EventHandler? StatusChanged;

    public void Start()
    {
        if (_runTask is { IsCompleted: false })
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                SetStatus(MarketFeedStatus.Connecting);
                socket = new ClientWebSocket();
                var url = BuildStreamUrl();
                await socket.ConnectAsync(new Uri(url), cancellationToken);
                SetStatus(MarketFeedStatus.Connected);
                PublishLog($"Connected to Binance Futures ({_symbols.Count} symbols)");

                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(MarketFeedStatus.Error);
                PublishLog($"Binance feed error: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                SetStatus(MarketFeedStatus.Reconnecting);
            }
            finally
            {
                if (socket is not null)
                {
                    try
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // ignore
                    }

                    socket.Dispose();
                }
            }
        }

        SetStatus(MarketFeedStatus.Stopped);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            var json = builder.ToString();
            if (!BinanceFuturesStreamParser.TryParseTickerMessage(json, out var ticker))
            {
                continue;
            }

            var spread = ticker.LastPrice * 0.0001m;
            _bus.Publish(new MarketTickEvent(
                ticker.Symbol,
                ticker.LastPrice,
                ticker.LastPrice - spread,
                ticker.LastPrice + spread,
                ticker.ChangePercent24h,
                ticker.QuoteVolume24h));
        }
    }

    private string BuildStreamUrl()
    {
        var streams = _symbols
            .Select(s => $"{s.ToLowerInvariant()}@ticker")
            .ToArray();
        return CombinedStreamBase + string.Join('/', streams);
    }

    private void SetStatus(MarketFeedStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishLog(string message) =>
        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Market",
            Source = "BinanceFutures",
            Message = message,
        }));

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        SetStatus(MarketFeedStatus.Stopped);
    }
}
