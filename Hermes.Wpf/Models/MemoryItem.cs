using System.Globalization;
using System.IO;

namespace Hermes.Wpf.Models;

/// <summary>Orchestratable memory capsule parsed from Markdown (e.g. Obsidian vault).</summary>
public sealed class MemoryItem
{
    /// <summary>semantic | procedural | episodic | identity from YAML, or empty.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Primary sort/display time (local).</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Body text extracted from Markdown (YAML + leading # lines removed).</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>Normalized lower-case hashtags without leading #, merged with YAML tags.</summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>Optional project from YAML.</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>1–5 from YAML; default 3 when missing.</summary>
    public int Importance { get; init; } = 3;

    /// <summary>Full filesystem path.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Unmodified file text.</summary>
    public string RawMarkdown { get; init; } = string.Empty;

    public string DateGroupKey => Timestamp.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>First non-empty meaningful line preview.</summary>
    public string PreviewLine
    {
        get
        {
            foreach (var line in Content.ReplaceLineEndings("\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0)
                {
                    return t.Length > 120 ? t[..120] + "…" : t;
                }
            }

            return Path.GetFileName(SourceFile);
        }
    }

    /// <summary>Shown in list subtitle.</summary>
    public string TagsDisplay =>
        Tags.Count == 0 ? string.Empty : string.Join(" • ", Tags.Select(t => $"#{t}"));
}
