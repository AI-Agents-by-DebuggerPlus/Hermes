namespace Hermes.SpotTerminal.Shared.Bridge;

public sealed class SkillsSnapshotSection
{
    public int DraftCount { get; init; }
    public int ApprovedCount { get; init; }
    public IReadOnlyList<SkillSnapshot> Skills { get; init; } = [];
}

public sealed class SkillSnapshot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsInitial { get; init; }
}
