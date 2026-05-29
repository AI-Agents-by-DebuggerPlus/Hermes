namespace Hermes.SpotTerminal.Core.Domain;

public sealed class PlatformLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string EventType { get; set; } = "";
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}
