using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Data.Projections;

public sealed class EventLogProjection
{
    private readonly ITradingStateStore _store;
    private readonly IEventBus _bus;

    public EventLogProjection(ITradingStateStore store, IEventBus bus)
    {
        _store = store;
        _bus = bus;
        _bus.SubscribeAll(OnEvent);
    }

    private void OnEvent(IPlatformEvent platformEvent)
    {
        if (platformEvent is PlatformLogEvent)
        {
            return;
        }

        var entry = platformEvent switch
        {
            MarketTickEvent tick => new PlatformLogEntry
            {
                Timestamp = tick.OccurredAt,
                EventType = "Market",
                Source = "MockFeed",
                Message = $"{tick.Symbol} {tick.Price:N2}",
            },
            OrderPlacedEvent placed => new PlatformLogEntry
            {
                Timestamp = placed.OccurredAt,
                EventType = "Order",
                Source = "VirtualExchange",
                Message = $"Order {placed.Order.Id} placed ({placed.Order.Symbol} {placed.Order.Side} {placed.Order.Quantity})",
            },
            OrderFilledEvent filled => new PlatformLogEntry
            {
                Timestamp = filled.OccurredAt,
                EventType = "Order",
                Source = "VirtualExchange",
                Message =
                    $"Order {filled.Order.Id} filled @ {filled.FillPrice:N2} ({filled.JournalKind}) "
                    + $"realized={filled.RealizedPnl:N4} fee={filled.Fee:N4} balance {filled.BalanceBefore:N2}→{filled.BalanceAfter:N2}",
            },
            PositionClosedEvent closed => new PlatformLogEntry
            {
                Timestamp = closed.OccurredAt,
                EventType = "Position",
                Source = "VirtualExchange",
                Message = $"Position closed {closed.Symbol} realized={closed.RealizedPnl:N4}",
            },
            OrderCancelledEvent cancelled => new PlatformLogEntry
            {
                Timestamp = cancelled.OccurredAt,
                EventType = "Order",
                Source = "VirtualExchange",
                Message = $"Order {cancelled.OrderId} cancelled ({cancelled.Symbol})",
            },
            RiskTriggeredEvent risk => new PlatformLogEntry
            {
                Timestamp = risk.OccurredAt,
                EventType = "Risk",
                Source = "RiskManager",
                Message = risk.Reason,
            },
            StrategySignalEvent signal => new PlatformLogEntry
            {
                Timestamp = signal.OccurredAt,
                EventType = "Strategy",
                Source = signal.StrategyName,
                Message = $"{signal.Symbol} {signal.Side} {signal.OrderType} qty {signal.Quantity} — {signal.Reason}",
            },
            _ => null,
        };

        if (entry is null)
        {
            return;
        }

        _store.Mutate(s =>
        {
            s.Logs.Insert(0, entry);
            if (s.Logs.Count > 200)
            {
                s.Logs.RemoveRange(200, s.Logs.Count - 200);
            }
        });
    }
}
