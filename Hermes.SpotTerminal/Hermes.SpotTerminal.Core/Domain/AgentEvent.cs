using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Core.Domain;

public sealed class AgentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset TimestampUtc { get; set; }
    public AgentEventKind Kind { get; set; }
    public string SessionId { get; set; } = "";
    public string? Symbol { get; set; }
    public string Summary { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
}
