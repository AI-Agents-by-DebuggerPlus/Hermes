namespace Hermes.SpotTerminal.Core.Events;

public interface IPlatformEvent
{
    DateTimeOffset TimestampUtc { get; }
}
