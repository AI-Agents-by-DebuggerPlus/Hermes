namespace Hermes.TradingPlatform.Core.Events;

public abstract record PlatformEventBase : IPlatformEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public abstract string EventType { get; }
}
