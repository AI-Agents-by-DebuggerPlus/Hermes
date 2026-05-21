using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;
using Hermes.TradingPlatform.Core.State;

namespace Hermes.TradingPlatform.Data.Projections;

public sealed class MarketTickProjection
{
    private readonly ITradingStateStore _store;
    private readonly IEventBus _bus;

    public MarketTickProjection(ITradingStateStore store, IEventBus bus)
    {
        _store = store;
        _bus = bus;
        _bus.Subscribe<MarketTickEvent>(OnTick);
    }

    private void OnTick(MarketTickEvent tick)
    {
        _store.Mutate(state =>
        {
            var ticker = state.Tickers.FirstOrDefault(t => t.Symbol == tick.Symbol);
            if (ticker is not null)
            {
                ticker.Price = tick.Price;
                if (tick.ChangePercent24h.HasValue)
                {
                    ticker.ChangePercent24h = tick.ChangePercent24h.Value;
                }

                if (tick.QuoteVolume24h.HasValue)
                {
                    ticker.Volume24h = tick.QuoteVolume24h.Value;
                }
            }

            foreach (var position in state.Positions.Where(p => p.Symbol == tick.Symbol))
            {
                position.MarkPrice = tick.Price;
                var direction = position.Side == PositionSide.Long ? 1m : -1m;
                position.UnrealizedPnl = (tick.Price - position.EntryPrice) * position.Size * direction;
            }

            TradingStateCalculator.RecalculateEquity(state);
        });
    }
}
