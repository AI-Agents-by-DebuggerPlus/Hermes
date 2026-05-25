namespace Hermes.Wpf.Models;

/// <summary>On-disk manifest for a generated Hermes skill (agentskills.io-inspired layout).</summary>
public sealed class GeneratedSkillManifest
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Category { get; init; } = "Generated";
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public DateTime CreatedAtUtc { get; init; }
    public List<string> Triggers { get; init; } = [];
    /// <summary>prompt | script | intent</summary>
    public string Kind { get; init; } = "prompt";
    public string ScriptFile { get; init; } = string.Empty;
    public string OutboundPromptBlock { get; init; } = string.Empty;
    public string TestCommand { get; init; } = string.Empty;
    public string SourceTurn { get; init; } = string.Empty;

    /// <summary>Optional role filter; empty or Universal-only → available in all roles.</summary>
    public List<AgentRole> Roles { get; init; } = [];

    public string DirectoryPath { get; init; } = string.Empty;
}
