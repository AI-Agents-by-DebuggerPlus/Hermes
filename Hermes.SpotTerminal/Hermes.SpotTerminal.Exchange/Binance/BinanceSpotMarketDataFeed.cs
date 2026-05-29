using Binance.Net.Interfaces;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Exchange.Binance;

public sealed class BinanceSpotMarketDataFeed : IMarketDataFeed
{
    private readonly IEventBus _bus;
    private readonly ISpotStateStore _store;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private global::Binance.Net.Clients.BinanceSocketClient? _socket;
    private readonly List<CryptoExchange.Net.Objects.Sockets.UpdateSubscription> _subs = [];

    public BinanceSpotMarketDataFeed(string apiKey, string apiSecret, IEventBus bus, ISpotStateStore store)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _bus = bus;
        _store = store;
    }

    public string Name => "Binance Spot Demo";

    public async Task StartAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        _socket = BinanceSpotClientFactory.CreateSocket(_apiKey, _apiSecret);
        _store.Mutate(s => s.FeedStatus = "Connecting (Binance Spot Demo)");

        foreach (var symbol in symbols)
        {
            var sym = symbol;
            var tickerSub = await _socket.SpotApi.ExchangeData.SubscribeToTickerUpdatesAsync(sym, data =>
            {
                var t = data.Data;
                _bus.Publish(new MarketTickEvent(sym, t.LastPrice, t.PriceChangePercent, t.Volume));
            }, ct);
            if (tickerSub.Success && tickerSub.Data is not null)
            {
                _subs.Add(tickerSub.Data);
            }

            var tradeSub = await _socket.SpotApi.ExchangeData.SubscribeToTradeUpdatesAsync(sym, data =>
            {
                var tr = data.Data;
                _bus.Publish(new MarketTickEvent(sym, tr.Price, 0m, tr.Quantity));
            }, ct);
            if (tradeSub.Success && tradeSub.Data is not null)
            {
                _subs.Add(tradeSub.Data);
            }

            var klineSub = await _socket.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(sym, global::Binance.Net.Enums.KlineInterval.OneMinute, data =>
            {
                var k = data.Data.Data;
                _bus.Publish(new MarketTickEvent(sym, k.ClosePrice, 0m, k.Volume));
            }, ct);
            if (klineSub.Success && klineSub.Data is not null)
            {
                _subs.Add(klineSub.Data);
            }

            var depthSub = await _socket.SpotApi.ExchangeData.SubscribeToOrderBookUpdatesAsync(sym, 10, data =>
            {
                var book = data.Data;
                var best = book.Bids.FirstOrDefault()?.Price ?? book.Asks.FirstOrDefault()?.Price ?? 0m;
                if (best > 0)
                {
                    _bus.Publish(new MarketTickEvent(sym, best, 0m, 0m));
                }
            }, ct);
            if (depthSub.Success && depthSub.Data is not null)
            {
                _subs.Add(depthSub.Data);
            }
        }

        _store.Mutate(s => s.FeedStatus = "Connected (Binance Spot Demo)");
    }

    public async Task StopAsync()
    {
        if (_socket is not null)
        {
            foreach (var sub in _subs)
            {
                await _socket.UnsubscribeAsync(sub);
            }

            _subs.Clear();
            _socket.Dispose();
            _socket = null;
        }

        _store.Mutate(s => s.FeedStatus = "Disconnected");
    }

    public void Dispose() => _socket?.Dispose();
}
