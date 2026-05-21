using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Events;

public sealed record MarketTickEvent(
    string Symbol,
    decimal Price,
    decimal Bid,
    decimal Ask,
    decimal? ChangePercent24h = null,
    decimal? QuoteVolume24h = null) : PlatformEventBase
{
    public override string EventType => "MarketTick";
}

public sealed record OrderPlacedEvent(Order Order) : PlatformEventBase
{
    public override string EventType => "OrderPlaced";
}

public sealed record OrderFilledEvent(
    Order Order,
    decimal FillPrice,
    decimal Fee,
    decimal RealizedPnl,
    decimal BalanceBefore,
    decimal BalanceAfter,
    string JournalKind) : PlatformEventBase
{
    public override string EventType => "OrderFilled";
}

public sealed record OrderCancelledEvent(string OrderId, string Symbol) : PlatformEventBase
{
    public override string EventType => "OrderCancelled";
}

public sealed record PositionClosedEvent(string Symbol, decimal RealizedPnl) : PlatformEventBase
{
    public override string EventType => "PositionClosed";
}

public sealed record RiskTriggeredEvent(string Reason, bool EmergencyHalt) : PlatformEventBase
{
    public override string EventType => "RiskTriggered";
}

public sealed record PlatformLogEvent(PlatformLogEntry Entry) : PlatformEventBase
{
    public override string EventType => "Log";
}
