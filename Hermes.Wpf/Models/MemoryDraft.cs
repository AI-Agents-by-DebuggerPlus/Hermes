namespace Hermes.Wpf.Models;

/// <summary>Draft extracted from chat or edited by the user before saving to the vault.</summary>
public sealed class MemoryDraft
{
    /// <summary>procedural | semantic | episodic | identity</summary>
    public string Type { get; set; } = "procedural";

    public string Problem { get; set; } = string.Empty;

    public string Solution { get; set; } = string.Empty;

    public string Reusable { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string Project { get; set; } = string.Empty;

    /// <summary>1–5</summary>
    public int Importance { get; set; } = 3;

    /// <summary>ISO 8601 for frontmatter.</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
