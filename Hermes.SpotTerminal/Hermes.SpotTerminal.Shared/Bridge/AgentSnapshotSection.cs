namespace Hermes.SpotTerminal.Shared.Bridge;

public sealed class AgentSnapshotSection
{
    public string SessionId { get; init; } = "";
    public string SessionState { get; init; } = "Idle";
    public string? ActiveSkillId { get; init; }
    public string CurrentThought { get; init; } = "";
    public IReadOnlyList<AgentEventSnapshot> RecentEvents { get; init; } = [];
}

public sealed class AgentEventSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string Kind { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Symbol { get; init; }
}
