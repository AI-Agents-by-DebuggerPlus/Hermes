using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Data.Projections;

public sealed class MarketTickProjection
{
    public MarketTickProjection(ISpotStateStore store, IEventBus bus)
    {
        bus.Subscribe<MarketTickEvent>(e =>
        {
            store.Mutate(s =>
            {
                var t = s.Tickers.FirstOrDefault(x => x.Symbol == e.Symbol);
                if (t is null)
                {
                    s.Tickers.Add(new MarketTicker
                    {
                        Symbol = e.Symbol,
                        Price = e.Price,
                        ChangePercent24h = e.ChangePercent24h,
                        Volume24h = e.Volume24h,
                        InWatchlist = true,
                    });
                }
                else
                {
                    t.Price = e.Price;
                    t.ChangePercent24h = e.ChangePercent24h;
                    t.Volume24h = e.Volume24h;
                }
            });
        });
    }
}
