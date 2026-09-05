using System.Text.Json.Serialization;

namespace Hermes.Wpf.Models;

public enum PortfolioCategory
{
    Idea = 0,
    InDevelopment = 1,
    Current = 2,
    Archive = 3,
}

/// <summary>Product initiative tracked by ProjectManager agent (not an Agent Workspace chat).</summary>
public sealed class PortfolioInitiative
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public PortfolioCategory Category { get; set; } = PortfolioCategory.Idea;

    /// <summary>Optional Agent Workspace name that works on this initiative (MVP link).</summary>
    public string? LinkedWorkspace { get; set; }

    public DateTime UpdatedAtLocal { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string CategoryLabel => Category switch
    {
        PortfolioCategory.Idea => "Idea",
        PortfolioCategory.InDevelopment => "In development",
        PortfolioCategory.Current => "Current",
        PortfolioCategory.Archive => "Archive",
        _ => Category.ToString(),
    };
}
