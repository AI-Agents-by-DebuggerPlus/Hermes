namespace Hermes.TradingPlatform.Core.Domain;

public sealed class PlatformLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public required string EventType { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}
