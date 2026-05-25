using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Shared lexical memory scoring for External Brain and role router.</summary>
public static class MemoryLexicalScorer
{
    public static IEnumerable<(MemoryItem M, double Score)> Score(
        ImmutableList<string> tokens,
        IReadOnlyList<MemoryItem> items)
    {
        if (tokens.IsEmpty)
        {
            return items.Select(static m => (m, RankMetaOnly(m)));
        }

        return items.Select(m =>
        {
            var hay = (m.Content + "\n" + m.Type + '\n' + m.Project + '\n' + string.Join(' ', m.Tags)).ToLowerInvariant();
            var file = Path.GetFileName(m.SourceFile).ToLowerInvariant();
            var relevance = 0.0;
            foreach (var token in tokens)
            {
                if (hay.Contains(token, StringComparison.Ordinal))
                {
                    relevance += 3;
                }

                if (file.Contains(token, StringComparison.Ordinal))
                {
                    relevance += 2;
                }

                if (m.Tags.Exists(tag => tag.Contains(token, StringComparison.Ordinal)))
                {
                    relevance += 4;
                }
            }

            var imp = Math.Max(1, m.Importance);
            var rec = RecencyMultiplier(m);
            var combined = (relevance + 0.01) * (0.45 + imp * 0.25) * rec;
            return (m, combined);
        });
    }

    private static double RankMetaOnly(MemoryItem m)
    {
        var imp = Math.Max(1, m.Importance);
        return imp * RecencyMultiplier(m);
    }

    private static double RecencyMultiplier(MemoryItem m)
    {
        var age = DateTime.UtcNow - m.Timestamp.ToUniversalTime();
        if (age.TotalDays < 1)
        {
            return 1.35;
        }

        if (age.TotalDays < 7)
        {
            return 1.15;
        }

        if (age.TotalDays < 30)
        {
            return 1.0;
        }

        return 0.85;
    }
}
