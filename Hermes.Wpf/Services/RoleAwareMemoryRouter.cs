using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Role-boosted filtering of vault memories for prompt injection.</summary>
public sealed class RoleAwareMemoryRouter
{
    private static readonly Regex TokenSplit = new(@"[\s,.;:!?()[\]{}""'`]+", RegexOptions.Compiled);

    private static readonly Dictionary<AgentRole, string[]> RolePrimaryTags = new()
    {
        [AgentRole.Trader] = ["trading", "market", "strategy", "pnl", "position", "order", "risk"],
        [AgentRole.Developer] = ["dotnet", "csharp", "code", "wpf", "wsl", "git", "debug", "architecture"],
        [AgentRole.EnglishTutor] = ["english", "vocabulary", "grammar", "exercise", "pronunciation"],
        [AgentRole.PersonalManager] = ["task", "project", "goal", "productivity", "deadline", "habit"],
        [AgentRole.Biohacker] =
        [
            "health", "supplement", "nootropic", "sleep", "nutrition", "exercise",
            "cognitive", "energy", "mood", "recovery", "биохакинг", "бад", "ноотроп",
            "сон", "питание", "тренировка", "самочувствие", "продуктивность", "здоровье",
        ],
        [AgentRole.Universal] = [],
    };

    private static readonly Dictionary<AgentRole, string[]> RoleVaultPaths = new()
    {
        [AgentRole.Trader] = ["Knowledge/Trading", "Procedures/Trading", "Projects/Trading"],
        [AgentRole.Developer] = ["Knowledge/Development", "Procedures/Dev", "Projects"],
        [AgentRole.EnglishTutor] = ["Knowledge/English", "Procedures/English"],
        [AgentRole.PersonalManager] = ["Knowledge/Productivity", "Projects", "Identity"],
        [AgentRole.Biohacker] =
        [
            "Health/Supplements", "Health/Protocols", "Health/Journal",
            "Health/Schedule", "Health/Goals", "Health/Metrics", "Identity",
        ],
        [AgentRole.Universal] = [],
    };

    private static readonly Dictionary<AgentRole, string[]> RolePenaltyTags = new()
    {
        [AgentRole.Trader] = ["english", "vocabulary", "grammar"],
        [AgentRole.Developer] = ["trading", "market", "pnl"],
        [AgentRole.EnglishTutor] = ["trading", "dotnet", "csharp"],
        [AgentRole.PersonalManager] = ["trading", "english"],
        [AgentRole.Biohacker] = ["trading", "dotnet", "csharp", "english", "vocabulary"],
        [AgentRole.Universal] = [],
    };

    public AgentRole CurrentRole { get; set; } = AgentRole.Universal;

    public IReadOnlyList<MemoryItem> FilterAndBoost(
        IReadOnlyList<MemoryItem> allItems,
        string userQuery,
        int maxItems)
    {
        if (allItems.Count == 0)
        {
            return [];
        }

        var cap = Math.Clamp(maxItems, 1, 20);
        if (CurrentRole == AgentRole.Universal)
        {
            var tokensU = Tokenize(userQuery);
            return MemoryLexicalScorer.Score(tokensU, allItems)
                .OrderByDescending(kv => kv.Score)
                .ThenByDescending(kv => kv.M.Timestamp)
                .Take(cap)
                .Select(kv => kv.M)
                .ToList();
        }

        var tokens = Tokenize(userQuery);
        var normalized = NormalizeScores(MemoryLexicalScorer.Score(tokens, allItems));
        var boosted = normalized
            .Select(kv => (kv.M, Score: ApplyRoleAdjustments(kv.M, kv.Score)))
            .OrderByDescending(kv => kv.Score)
            .ThenByDescending(kv => kv.M.Timestamp)
            .ThenByDescending(kv => kv.M.Importance)
            .Take(cap)
            .Select(kv => kv.M)
            .ToList();

        return boosted;
    }

    public string GetRoleTag(AgentRole role) =>
        role switch
        {
            AgentRole.Trader => "trading",
            AgentRole.Developer => "development",
            AgentRole.EnglishTutor => "english",
            AgentRole.PersonalManager => "productivity",
            AgentRole.Biohacker => "health",
            _ => "universal",
        };

    private double ApplyRoleAdjustments(MemoryItem item, double baseScore)
    {
        var score = baseScore;
        if (MatchesRolePrimary(item))
        {
            score += 0.3;
        }

        if (MatchesForeignRole(item))
        {
            score *= 0.4;
        }

        return score;
    }

    private bool MatchesRolePrimary(MemoryItem item)
    {
        if (!RolePrimaryTags.TryGetValue(CurrentRole, out var tags) || tags.Length == 0)
        {
            return false;
        }

        if (item.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!RoleVaultPaths.TryGetValue(CurrentRole, out var paths))
        {
            return false;
        }

        var file = item.SourceFile.Replace('\\', '/');
        return paths.Any(p => file.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesForeignRole(MemoryItem item)
    {
        if (!RolePenaltyTags.TryGetValue(CurrentRole, out var penalty) || penalty.Length == 0)
        {
            return false;
        }

        return item.Tags.Any(t => penalty.Contains(t, StringComparer.OrdinalIgnoreCase));
    }

    private static List<(MemoryItem M, double Score)> NormalizeScores(IEnumerable<(MemoryItem M, double Score)> scored)
    {
        var list = scored.ToList();
        if (list.Count == 0)
        {
            return list;
        }

        var max = list.Max(x => x.Score);
        if (max <= 1e-9)
        {
            return list;
        }

        return list.Select(x => (x.M, x.Score / max)).ToList();
    }

    private static ImmutableList<string> Tokenize(string text) =>
        TokenSplit
            .Split((text ?? string.Empty).ToLowerInvariant())
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableList();
}
