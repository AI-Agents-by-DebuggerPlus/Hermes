namespace Hermes.SpotTerminal.Core.Domain;

public sealed class AgentSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string State { get; set; } = "Idle";
    public string? ActiveSkillId { get; set; }
    public string CurrentThought { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastEventAtUtc { get; set; }
}
