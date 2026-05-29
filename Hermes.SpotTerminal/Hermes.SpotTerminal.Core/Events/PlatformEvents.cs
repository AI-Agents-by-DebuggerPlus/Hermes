using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Core.Events;

public sealed record PlatformLogEvent(PlatformLogEntry Entry) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc => Entry.Timestamp;
}

public sealed record AgentEventRecorded(AgentEvent Event) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc => Event.TimestampUtc;
}

public sealed record MarketTickEvent(string Symbol, decimal Price, decimal ChangePercent24h, decimal Volume24h) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderPlacedEvent(SpotOrder Order) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc => Order.CreatedAt;
}

public sealed record OrderFilledEvent(SpotOrder Order) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderCancelledEvent(string OrderId) : IPlatformEvent
{
    public DateTimeOffset TimestampUtc { get; } = DateTimeOffset.UtcNow;
}

public sealed record BalancesUpdatedEvent : IPlatformEvent
{
    public DateTimeOffset TimestampUtc { get; } = DateTimeOffset.UtcNow;
}
