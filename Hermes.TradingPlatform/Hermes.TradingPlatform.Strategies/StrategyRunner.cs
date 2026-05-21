using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Strategies;

/// <summary>Runs enabled strategies on each market tick (Phase 5).</summary>
public sealed class StrategyRunner
{
    private readonly IEventBus _bus;
    private readonly ITradingStateStore _store;
    private readonly IVirtualExchange _exchange;
    private readonly IReadOnlyList<ITradingStrategy> _strategies;

    public StrategyRunner(
        IEventBus bus,
        ITradingStateStore store,
        IVirtualExchange exchange,
        IEnumerable<ITradingStrategy> strategies)
    {
        _bus = bus;
        _store = store;
        _exchange = exchange;
        _strategies = strategies.ToList();
        _bus.Subscribe<MarketTickEvent>(OnMarketTick);
    }

    public bool AutoExecuteEnabled { get; set; } = true;

    private void OnMarketTick(MarketTickEvent tick)
    {
        var snapshot = _store.Snapshot;
        if (snapshot.Risk.EmergencyHalt)
        {
            return;
        }

        foreach (var strategy in _strategies)
        {
            var signal = strategy.Evaluate(tick, snapshot);
            if (signal is null)
            {
                continue;
            }

            _bus.Publish(new StrategySignalEvent(
                strategy.Id,
                strategy.Name,
                signal.Symbol,
                signal.Side,
                signal.OrderType,
                signal.Quantity,
                signal.Price,
                signal.Reason,
                signal.AutoExecute && AutoExecuteEnabled));

            if (!signal.AutoExecute || !AutoExecuteEnabled)
            {
                continue;
            }

            var order = _exchange.PlaceOrder(
                signal.Symbol,
                signal.OrderType,
                signal.Side,
                signal.Quantity,
                signal.Price,
                reduceOnly: false);

            _store.Mutate(s =>
            {
                var card = s.Strategies.FirstOrDefault(x => x.Id == strategy.Id);
                if (card is not null && order.Status != OrderStatus.Rejected)
                {
                    card.Status = StrategyRunStatus.Running;
                }
            });
        }
    }
}
