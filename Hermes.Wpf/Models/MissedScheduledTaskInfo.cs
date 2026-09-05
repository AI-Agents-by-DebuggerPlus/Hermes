namespace Hermes.Wpf.Models;

public sealed class MissedScheduledTaskInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required DateTime ExpectedAtLocal { get; init; }
    public required MissedTaskKind Kind { get; init; }
    public string? SchTaskName { get; init; }
}

public enum MissedTaskKind
{
    ReniWaterMonthly,
    ReniWaterOnce,
}
