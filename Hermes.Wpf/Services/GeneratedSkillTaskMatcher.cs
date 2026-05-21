using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>TF-IDF + lexical ranking of saved skills against a user task.</summary>
public sealed class GeneratedSkillTaskMatcher
{
    private static readonly Regex TokenSplit = new(@"[\s,.;:!?()[\]{}""'`_\-]+", RegexOptions.Compiled);

    private readonly LogService _log;
    private readonly object _lock = new();

    private IReadOnlyList<GeneratedSkillManifest> _skills = [];
    private Dictionary<string, float[]> _tfidfById = new(StringComparer.OrdinalIgnoreCase);
    private string[] _vocabulary = [];
    private float[] _idf = [];

    public GeneratedSkillTaskMatcher(LogService log) => _log = log;

    public void Rebuild(IReadOnlyList<GeneratedSkillManifest> skills)
    {
        lock (_lock)
        {
            _skills = skills.Where(s => s.Enabled).ToList();
            BuildTfidfIndex(_skills);
        }

        _log.LogInfo($"[skill-resolver] index ready ({_skills.Count} enabled skill(s))");
    }

    public IReadOnlyList<SkillTaskMatch> Rank(string userTask, int maxItems, double minScore)
    {
        var text = (userTask ?? string.Empty).Trim();
        if (text.Length < 6)
        {
            return [];
        }

        List<GeneratedSkillManifest> skills;
        string[] vocabulary;
        float[] idf;
        Dictionary<string, float[]> tfidfById;
        lock (_lock)
        {
            skills = _skills.ToList();
            vocabulary = _vocabulary;
            idf = _idf;
            tfidfById = _tfidfById;
        }

        if (skills.Count == 0)
        {
            return [];
        }

        var cap = Math.Clamp(maxItems, 1, 8);
        var threshold = Math.Clamp(minScore, 0.05, 0.95);
        var queryTokens = Tokenize(text);
        var queryVector = BuildTfidfVector(queryTokens, vocabulary, idf);

        var ranked = new List<SkillTaskMatch>(skills.Count);
        foreach (var skill in skills)
        {
            if (!tfidfById.TryGetValue(skill.Id, out var docVector))
            {
                continue;
            }

            var tfidf = CosineSimilarity(queryVector, docVector);
            var lexical = LexicalScore(text, queryTokens, skill);
            var score = 0.55 * tfidf + 0.45 * lexical;
            if (score < threshold)
            {
                continue;
            }

            var reason = BuildReason(tfidf, lexical, skill);
            ranked.Add(new SkillTaskMatch(skill, score, reason));
        }

        var top = ranked
            .OrderByDescending(m => m.Score)
            .Take(cap)
            .ToList();

        if (top.Count > 0)
        {
            _log.LogInfo(
                $"[skill-resolver] task → {top[0].Skill.Id} (score={top[0].Score:F2}, {top[0].Reason})");
        }

        return top;
    }

    private void BuildTfidfIndex(IReadOnlyList<GeneratedSkillManifest> skills)
    {
        var docs = skills
            .Select(s => (s.Id, tokens: Tokenize(SkillSearchText(s))))
            .ToList();

        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, tokens) in docs)
        {
            foreach (var t in tokens.Distinct(StringComparer.Ordinal))
            {
                df.TryGetValue(t, out var c);
                df[t] = c + 1;
            }
        }

        _vocabulary = df
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(2048)
            .Select(kv => kv.Key)
            .ToArray();

        var n = Math.Max(1, docs.Count);
        _idf = new float[_vocabulary.Length];
        for (var i = 0; i < _vocabulary.Length; i++)
        {
            _idf[i] = (float)Math.Log((n + 1.0) / (df[_vocabulary[i]] + 1.0)) + 1f;
        }

        _tfidfById = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, tokens) in docs)
        {
            _tfidfById[id] = BuildTfidfVector(tokens, _vocabulary, _idf);
        }
    }

    private static string SkillSearchText(GeneratedSkillManifest skill)
    {
        var idParts = skill.Id.Replace('_', ' ').Replace('-', ' ');
        var triggers = string.Join(' ', skill.Triggers);
        return $"{skill.Id} {idParts} {skill.Title} {skill.Summary} {triggers} {skill.Kind} {skill.OutboundPromptBlock}";
    }

    private static double LexicalScore(string text, ImmutableList<string> queryTokens, GeneratedSkillManifest skill)
    {
        var hay = SkillSearchText(skill).ToLowerInvariant();
        var q = text.ToLowerInvariant();
        var score = 0.0;

        foreach (var trigger in skill.Triggers)
        {
            var t = trigger.Trim();
            if (t.Length >= 4 && q.Contains(t, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 0.95);
            }
        }

        foreach (var token in queryTokens)
        {
            if (token.Length < 3)
            {
                continue;
            }

            if (hay.Contains(token, StringComparison.Ordinal))
            {
                score += 0.12;
            }

            if (skill.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.18;
            }
        }

        score += ConceptAffinityBoost(q, hay);
        return Math.Min(1.0, score);
    }

    private static double ConceptAffinityBoost(string queryLower, string hayLower)
    {
        var boost = 0.0;
        if (ContainsAny(queryLower, "zip", "архив", "archive", "запак", "упаков", "pack", "сжат", "compress"))
        {
            if (ContainsAny(hayLower, "zip", "архив", "archive", "pack"))
            {
                boost += 0.45;
            }
        }

        if (ContainsAny(queryLower, "скрин", "screen", "экран", "screenshot", "снимок"))
        {
            if (ContainsAny(hayLower, "screen", "screenshot", "vision", "скрин"))
            {
                boost += 0.35;
            }
        }

        return boost;
    }

    private static bool ContainsAny(string text, params string[] parts)
    {
        foreach (var p in parts)
        {
            if (text.Contains(p, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildReason(double tfidf, double lexical, GeneratedSkillManifest skill)
    {
        if (lexical >= 0.9)
        {
            return "trigger/id match";
        }

        if (tfidf >= 0.35)
        {
            return "semantic+lexical";
        }

        return $"{skill.Kind}";
    }

    private static ImmutableList<string> Tokenize(string text) =>
        TokenSplit
            .Split(text.ToLowerInvariant())
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableList();

    private static float[] BuildTfidfVector(ImmutableList<string> tokens, string[] vocabulary, float[] idf)
    {
        if (vocabulary.Length == 0 || tokens.IsEmpty)
        {
            return [];
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tokens)
        {
            counts.TryGetValue(t, out var c);
            counts[t] = c + 1;
        }

        var total = Math.Max(1, tokens.Count);
        var vector = new float[vocabulary.Length];
        for (var i = 0; i < vocabulary.Length; i++)
        {
            if (!counts.TryGetValue(vocabulary[i], out var tf))
            {
                continue;
            }

            vector[i] = (tf / (float)total) * idf[i];
        }

        return Normalize(vector);
    }

    private static float[] Normalize(float[] vector)
    {
        if (vector.Length == 0)
        {
            return vector;
        }

        double sum = 0;
        foreach (var v in vector)
        {
            sum += v * v;
        }

        if (sum <= 1e-12)
        {
            return vector;
        }

        var norm = Math.Sqrt(sum);
        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = (float)(vector[i] / norm);
        }

        return result;
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot;
    }
}
