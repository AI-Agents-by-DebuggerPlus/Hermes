namespace Hermes.Wpf.Models;

/// <summary>Per-project UI extras (avatar, optional ecosystem override).</summary>
public sealed class ProjectUiMeta
{
    public string? AvatarPath { get; set; }

    /// <summary>Optional override of auto-detected ecosystem id (trading, english, …).</summary>
    public string? EcosystemId { get; set; }
}
