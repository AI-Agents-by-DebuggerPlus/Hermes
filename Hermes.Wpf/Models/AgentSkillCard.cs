namespace Hermes.Wpf.Models;

/// <summary>One capability card on the Skills tab (Hermes + desktop client).</summary>
public sealed class AgentSkillCard
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required string Category { get; init; }
}
