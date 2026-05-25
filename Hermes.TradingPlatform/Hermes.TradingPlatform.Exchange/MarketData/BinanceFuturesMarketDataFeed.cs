using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Exchange.MarketData;

/// <summary>Binance USDT-M Futures public ticker WebSocket (Phase 4).</summary>
public sealed class BinanceFuturesMarketDataFeed : IMarketDataFeed
{
    private const string SingleStreamBase = "wss://fstream.binance.com/ws";
    private const string RestBase = "https://fapi.binance.com";
    private static readonly TimeSpan TwentyFourHrPollInterval = TimeSpan.FromMinutes(1);

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly IEventBus _bus;
    private readonly IReadOnlyList<string> _symbols;
    private readonly Action<string>? _diagnosticLog;
    private readonly HashSet<string> _firstTickLogged = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource _cts = new();
    private Task? _runTask;
    private Task? _statsTask;

    public BinanceFuturesMarketDataFeed(IEventBus bus, IEnumerable<string> symbols, Action<string>? diagnosticLog = null)
    {
        _bus = bus;
        _diagnosticLog = diagnosticLog;
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
        _statsTask = Task.Run(() => Poll24hrStatsLoopAsync(_cts.Token));
    }

    private async Task Poll24hrStatsLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                foreach (var symbol in _symbols)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await PublishOne24hrStatAsync(symbol, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _diagnosticLog?.Invoke($"[binance] 24hr stats error: {ex.Message}");
            }

            try
            {
                await Task.Delay(TwentyFourHrPollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PublishOne24hrStatAsync(string symbol, CancellationToken cancellationToken)
    {
        var url = $"{RestBase}/fapi/v1/ticker/24hr?symbol={symbol}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var lastPriceStr = root.GetProperty("lastPrice").GetString();
        var changePercentStr = root.GetProperty("priceChangePercent").GetString();
        var quoteVolumeStr = root.GetProperty("quoteVolume").GetString();

        if (!decimal.TryParse(lastPriceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            return;
        }

        decimal? change = decimal.TryParse(changePercentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var c) ? c : null;
        decimal? volume = decimal.TryParse(quoteVolumeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;

        var spread = price * 0.0001m;
        _bus.Publish(new MarketTickEvent(
            symbol,
            price,
            price - spread,
            price + spread,
            change,
            volume));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        _diagnosticLog?.Invoke($"[binance] start, symbols={_symbols.Count} endpoint={SingleStreamBase}");
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                SetStatus(MarketFeedStatus.Connecting);
                _diagnosticLog?.Invoke("[binance] connecting…");
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                await socket.ConnectAsync(new Uri(SingleStreamBase), cancellationToken);
                SetStatus(MarketFeedStatus.Connected);
                PublishLog($"Connected to Binance Futures ({_symbols.Count} symbols)");
                _diagnosticLog?.Invoke($"[binance] connected, sending SUBSCRIBE for {_symbols.Count} symbols");

                await SendSubscribeAsync(socket, cancellationToken);

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
                _diagnosticLog?.Invoke($"[binance] error: {ex.GetType().Name}: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                SetStatus(MarketFeedStatus.Reconnecting);
                _diagnosticLog?.Invoke("[binance] reconnecting…");
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
        var framesSeen = 0;
        var lastDiagAt = DateTime.UtcNow;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            builder.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _diagnosticLog?.Invoke(
                        $"[binance] socket closed by server: {result.CloseStatus} '{result.CloseStatusDescription}' (framesSeen={framesSeen})");
                    return;
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            framesSeen++;
            var json = builder.ToString();
            if (framesSeen == 1 && _diagnosticLog is not null)
            {
                var preview = json.Length > 200 ? json[..200] + "…" : json;
                _diagnosticLog($"[binance] first frame received ({json.Length} bytes): {preview}");
            }
            else if (_diagnosticLog is not null && (DateTime.UtcNow - lastDiagAt) > TimeSpan.FromSeconds(60))
            {
                lastDiagAt = DateTime.UtcNow;
                _diagnosticLog($"[binance] heartbeat: framesSeen={framesSeen}");
            }

            if (!BinanceFuturesStreamParser.TryParseTickerMessage(json, out var ticker))
            {
                continue;
            }

            var spread = ticker.LastPrice * 0.0001m;
            decimal? change = ticker.ChangePercent24h != 0m ? ticker.ChangePercent24h : null;
            decimal? volume = ticker.QuoteVolume24h != 0m ? ticker.QuoteVolume24h : null;
            _bus.Publish(new MarketTickEvent(
                ticker.Symbol,
                ticker.LastPrice,
                ticker.LastPrice - spread,
                ticker.LastPrice + spread,
                change,
                volume));

            if (_diagnosticLog is not null && _firstTickLogged.Add(ticker.Symbol))
            {
                _diagnosticLog($"[binance] first tick {ticker.Symbol} @ {ticker.LastPrice} ({ticker.ChangePercent24h:+0.00;-0.00}% 24h)");
            }
        }
    }

    private async Task SendSubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        // Subscribe to bookTicker (per-update bid/ask) + ticker (1s 24h stats).
        // bookTicker is event-driven and most reliable for live price; ticker adds 24h percent change.
        var streamNames = new List<string>(_symbols.Count * 2);
        foreach (var symbol in _symbols)
        {
            var lower = symbol.ToLowerInvariant();
            streamNames.Add($"{lower}@bookTicker");
            streamNames.Add($"{lower}@ticker");
        }

        var streams = string.Join(",", streamNames.Select(s => $"\"{s}\""));
        var payload = $"{{\"method\":\"SUBSCRIBE\",\"params\":[{streams}],\"id\":1}}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        _diagnosticLog?.Invoke($"[binance] sent SUBSCRIBE ({streamNames.Count} streams) payload={payload}");
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
