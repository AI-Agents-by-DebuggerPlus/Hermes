namespace Hermes.Wpf.Models;

public sealed class ProjectEcosystemInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string AccentHex { get; init; }
    public required IReadOnlyList<string> ProjectNameHints { get; init; }
    public required IReadOnlyList<ProjectRelatedAppInfo> Apps { get; init; }
}

public sealed class ProjectRelatedAppInfo
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }

    /// <summary>Exe name under Hermes.Wpf base directory, if any.</summary>
    public string? ExeFileName { get; init; }

    /// <summary>Known alternate paths relative to repo root (dev).</summary>
    public IReadOnlyList<string>? DevRelativeExePaths { get; init; }
}
