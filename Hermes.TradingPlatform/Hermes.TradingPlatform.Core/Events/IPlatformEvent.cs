namespace Hermes.TradingPlatform.Core.Events;

public interface IPlatformEvent
{
    DateTimeOffset OccurredAt { get; }
    string EventType { get; }
}
