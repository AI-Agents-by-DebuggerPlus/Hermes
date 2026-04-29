namespace Hermes.Wpf.Models;

/// <summary>
/// One step in preflight / diagnostics.
/// </summary>
public sealed class CheckResult
{
    public required bool Ok { get; init; }
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public string? FixHint { get; init; }
}
