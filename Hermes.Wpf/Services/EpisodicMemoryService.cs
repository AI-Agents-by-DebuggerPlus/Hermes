using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.InAppAssistant;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Episodic session log + cross-session batch review (design §3): deterministic + optional OpenRouter.</summary>
internal sealed class EpisodicMemoryService
{
    public const int BatchEverySessions = 5;
    public const int PatternMinCount = 3;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly LogService _log;
    private readonly SkillsPendingService _skillsPending;
    private readonly Func<(string ApiKey, string Model)> _openRouterCredentials;
    private readonly object _gate = new();

    public EpisodicMemoryService(
        LogService log,
        SkillsPendingService skillsPending,
        Func<(string ApiKey, string Model)>? openRouterCredentials = null)
    {
        _log = log;
        _skillsPending = skillsPending;
        _openRouterCredentials = openRouterCredentials ?? (() => (string.Empty, OpenRouterChatClient.DefaultModel));
    }

    public void AppendEvent(
        string projectName,
        string? sessionId,
        string kind,
        string summary,
        object? extra = null)
    {
        try
        {
            var dir = ResolveEpisodicDir();
            if (dir is null)
            {
                return;
            }

            Directory.CreateDirectory(dir);
            var safeProject = SanitizeFilePart(projectName);
            var day = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var path = Path.Combine(dir, $"{day}_{safeProject}.jsonl");
            var record = new Dictionary<string, object?>
            {
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["project"] = projectName,
                ["session"] = sessionId ?? string.Empty,
                ["kind"] = kind,
                ["summary"] = Truncate(summary, 500),
            };
            if (extra is not null)
            {
                record["extra"] = extra;
            }

            var line = JsonSerializer.Serialize(record, JsonOpts) + "\n";
            lock (_gate)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[episodic] append failed: {ex.Message}");
        }
    }

    /// <summary>Call on session close. Returns user-facing note if batch staged something.</summary>
    public async Task<string?> OnSessionClosedAsync(
        string projectWindowsPath,
        string projectName,
        string? sessionId)
    {
        AppendEvent(projectName, sessionId, "session_closed", "CLI session closed");
        BumpSessionCounter();
        return await MaybeRunBatchReviewAsync(projectWindowsPath, projectName).ConfigureAwait(false);
    }

    public async Task<string?> MaybeRunBatchReviewAsync(
        string projectWindowsPath,
        string projectName,
        bool force = false)
    {
        try
        {
            var dir = ResolveEpisodicDir();
            if (dir is null)
            {
                return null;
            }

            var metaPath = Path.Combine(dir, "meta.json");
            var meta = ReadMeta(metaPath);
            if (!force && meta.SessionsSinceBatch < BatchEverySessions)
            {
                return null;
            }

            meta.SessionsSinceBatch = 0;
            meta.LastBatchUtc = DateTime.UtcNow.ToString("o");
            WriteMeta(metaPath, meta);

            var staged = 0;

            var patterns = FindCrossSessionPatterns(dir, lookbackDays: 21);
            foreach (var p in patterns.Take(3))
            {
                StageDeterministicDraft(projectWindowsPath, p);
                staged++;
            }

            var llmStaged = await TryAuxiliaryLlmBatchAsync(dir, projectWindowsPath, projectName)
                .ConfigureAwait(false);
            staged += llmStaged;

            if (patterns.Count == 0 && llmStaged == 0)
            {
                _log.LogInfo("[episodic] batch review: no patterns / no LLM drafts");
                AppendEvent(projectName, null, "batch_review", "no patterns", new { count = 0, llm = 0 });
                UpdateSimpleIndex(dir);
                return null;
            }

            AppendEvent(
                projectName,
                null,
                "batch_review",
                $"staged {staged} skill_draft(s)",
                new
                {
                    deterministic = patterns.Select(x => new { x.What, x.CorrectedTo, x.Count }),
                    llm = llmStaged,
                });

            UpdateSimpleIndex(dir);
            return staged > 0
                ? $"Episodic batch: staged {staged} skill_draft(s) (pending auto-approve ×{SkillsPendingService.AutoApproveAfter})."
                : null;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[episodic] batch failed: {ex.Message}");
            return null;
        }
    }

    private void StageDeterministicDraft(string projectWindowsPath, PatternHit p)
    {
        var draftName = $"episodic-{SanitizeFilePart(p.What)}";
        var content =
            $"## When to use\n\n"
            + $"User repeatedly corrected «{p.What}» toward «{p.CorrectedTo}» "
            + $"({p.Count} times across sessions).\n\n"
            + $"## Do\n\n"
            + $"- Prefer «{p.CorrectedTo}» over «{p.ActualFirst}» when this pattern appears.\n"
            + $"- Do not invent values; confirm from the current screenshot/context.\n";

        var outcome = new ReflectionOutcomeParser.Outcome(
            "skill_draft",
            $"episodic batch ×{p.Count}",
            draftName,
            content,
            string.Empty);
        var (applied, message) = _skillsPending.ProcessReflectionOutcome(projectWindowsPath, outcome);
        _log.LogInfo($"[episodic] batch staged {draftName}: {applied} {message}");
    }

    private async Task<int> TryAuxiliaryLlmBatchAsync(
        string episodicDir,
        string projectWindowsPath,
        string projectName)
    {
        var (apiKey, model) = _openRouterCredentials();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _log.LogInfo("[episodic] LLM batch skipped (no OpenRouter key)");
            return 0;
        }

        var digest = BuildEpisodicDigest(episodicDir, lookbackDays: 21, maxChars: 6000);
        if (string.IsNullOrWhiteSpace(digest))
        {
            return 0;
        }

        var prompt =
            "You review Hermes agent episodic logs across sessions.\n"
            + "Find repeating cross-session mistakes or user corrections that warrant a reusable skill.\n"
            + "Return ONLY a JSON array (max 3 items). Each item:\n"
            + "{\"name\":\"slug\",\"reason\":\"…\",\"content\":\"markdown skill body with ## When to use and ## Do\"}\n"
            + "If nothing reusable, return []. Do not invent project-specific one-offs.\n\n"
            + $"Project focus: {projectName}\n\n"
            + "LOG DIGEST:\n"
            + digest;

        try
        {
            using var client = new OpenRouterChatClient();
            var raw = await client.CompleteAsync(
                    new AppAssistantOptions
                    {
                        OpenRouterApiKey = apiKey.Trim(),
                        Model = string.IsNullOrWhiteSpace(model) ? OpenRouterChatClient.DefaultModel : model,
                    },
                    new[]
                    {
                        new AssistantChatMessage(
                            "system",
                            "You propose Hermes skill drafts from episodic patterns. JSON array only."),
                        new AssistantChatMessage("user", prompt),
                    })
                .ConfigureAwait(false);

            var drafts = ParseLlmSkillDrafts(raw);
            var staged = 0;
            foreach (var d in drafts.Take(3))
            {
                var outcome = new ReflectionOutcomeParser.Outcome(
                    "skill_draft",
                    d.Reason,
                    d.Name,
                    d.Content,
                    string.Empty);
                var (applied, message) = _skillsPending.ProcessReflectionOutcome(projectWindowsPath, outcome);
                _log.LogInfo($"[episodic] LLM batch staged {d.Name}: {applied} {message}");
                staged++;
            }

            return staged;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[episodic] LLM batch failed: {ex.Message}");
            return 0;
        }
    }

    private static string BuildEpisodicDigest(string dir, int lookbackDays, int maxChars)
    {
        var since = DateTime.UtcNow.Date.AddDays(-lookbackDays);
        var sb = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl")
                     .OrderByDescending(f => f)
                     .Take(40))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("ts", out var tsEl)
                        && DateTime.TryParse(tsEl.GetString(), out var ts)
                        && ts.ToUniversalTime() < since)
                    {
                        continue;
                    }

                    var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                    if (kind is not ("implicit_correction" or "note_correction" or "correction"
                        or "reflection" or "session_closed"))
                    {
                        continue;
                    }

                    var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var project = root.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "";
                    sb.AppendLine($"[{kind}] {project}: {Truncate(summary, 200)}");
                    if (sb.Length >= maxChars)
                    {
                        return sb.ToString(0, maxChars);
                    }
                }
                catch
                {
                    // skip
                }
            }
        }

        return sb.ToString();
    }

    private static List<(string Name, string Reason, string Content)> ParseLlmSkillDrafts(string raw)
    {
        var list = new List<(string, string, string)>();
        var text = (raw ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            text = Regex.Replace(text, @"^```(?:json)?\s*", "");
            text = Regex.Replace(text, @"\s*```$", "");
        }

        JsonElement arr;
        try
        {
            using var doc = JsonDocument.Parse(text);
            arr = doc.RootElement.Clone();
        }
        catch
        {
            var m = Regex.Match(text, @"\[.*\]", RegexOptions.Singleline);
            if (!m.Success)
            {
                return list;
            }

            try
            {
                using var doc = JsonDocument.Parse(m.Value);
                arr = doc.RootElement.Clone();
            }
            catch
            {
                return list;
            }
        }

        if (arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var reason = el.TryGetProperty("reason", out var r) ? r.GetString() ?? "episodic LLM" : "episodic LLM";
            var content = el.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            list.Add((SanitizeFilePart(name), reason.Trim(), content.Trim()));
        }

        return list;
    }

    private List<PatternHit> FindCrossSessionPatterns(string dir, int lookbackDays)
    {
        var since = DateTime.UtcNow.Date.AddDays(-lookbackDays);
        var bag = new Dictionary<string, PatternHit>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
            var name = Path.GetFileName(file);
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                    if (!kind.Equals("correction", StringComparison.OrdinalIgnoreCase)
                        && !kind.Equals("implicit_correction", StringComparison.OrdinalIgnoreCase)
                        && !kind.Equals("note_correction", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var tsText = root.TryGetProperty("ts", out var tsEl) ? tsEl.GetString() : null;
                    if (tsText is not null
                        && DateTime.TryParse(tsText, out var ts)
                        && ts.ToUniversalTime() < since)
                    {
                        continue;
                    }

                    string what = string.Empty, actual = string.Empty, corrected = string.Empty;
                    if (root.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object)
                    {
                        what = ReadStr(extra, "what");
                        actual = ReadStr(extra, "actual_first");
                        corrected = ReadStr(extra, "corrected_to");
                    }

                    if (string.IsNullOrWhiteSpace(corrected) && string.IsNullOrWhiteSpace(what))
                    {
                        continue;
                    }

                    var key = $"{what}|{corrected}".ToLowerInvariant();
                    if (!bag.TryGetValue(key, out var hit))
                    {
                        hit = new PatternHit(what, actual, corrected, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                        bag[key] = hit;
                    }

                    var session = root.TryGetProperty("session", out var s) ? s.GetString() ?? "" : "";
                    var project = root.TryGetProperty("project", out var p) ? p.GetString() ?? "" : "";
                    hit.Sessions.Add($"{project}:{session}:{tsText ?? name}");
                    hit = hit with { Count = hit.Sessions.Count };
                    bag[key] = hit;
                }
                catch
                {
                    // skip bad line
                }
            }
        }

        TryFoldProjectCorrections(bag, since);

        return bag.Values
            .Where(h => h.Count >= PatternMinCount)
            .OrderByDescending(h => h.Count)
            .ToList();
    }

    private void TryFoldProjectCorrections(Dictionary<string, PatternHit> bag, DateTime since)
    {
        try
        {
            var roots = new[]
            {
                @"D:\Programming\AI_Agents\HermesProjects",
            };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var corr in Directory.EnumerateFiles(root, "corrections.jsonl", SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadLines(corr))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        try
                        {
                            var e = JsonSerializer.Deserialize<CorrectionLogEntry>(line);
                            if (e is null)
                            {
                                continue;
                            }

                            if (DateTime.TryParse(e.Ts, out var ts) && ts.ToUniversalTime() < since)
                            {
                                continue;
                            }

                            var key = $"{e.What}|{e.CorrectedTo}".ToLowerInvariant();
                            if (!bag.TryGetValue(key, out var hit))
                            {
                                hit = new PatternHit(e.What, e.ActualFirst, e.CorrectedTo, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                                bag[key] = hit;
                            }

                            hit.Sessions.Add($"{e.Project}:{e.Session}:{e.Ts}");
                            hit = hit with { Count = hit.Sessions.Count };
                            bag[key] = hit;
                        }
                        catch
                        {
                            // skip
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[episodic] fold corrections: {ex.Message}");
        }
    }

    private void UpdateSimpleIndex(string dir)
    {
        try
        {
            var tokens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl").TakeLast(30))
            {
                var text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text.ToLowerInvariant(), @"[a-zа-яё0-9_]{3,}"))
                {
                    var t = m.Value;
                    tokens[t] = tokens.TryGetValue(t, out var c) ? c + 1 : 1;
                }
            }

            var top = tokens.OrderByDescending(kv => kv.Value).Take(200)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(
                Path.Combine(dir, "index.tfidf.json"),
                JsonSerializer.Serialize(new { updated = DateTime.UtcNow.ToString("o"), term_freq = top }, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[episodic] index: {ex.Message}");
        }
    }

    private void BumpSessionCounter()
    {
        var dir = ResolveEpisodicDir();
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var metaPath = Path.Combine(dir, "meta.json");
        var meta = ReadMeta(metaPath);
        meta.SessionsSinceBatch++;
        WriteMeta(metaPath, meta);
    }

    private static EpisodicMeta ReadMeta(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new EpisodicMeta();
            }

            return JsonSerializer.Deserialize<EpisodicMeta>(File.ReadAllText(path)) ?? new EpisodicMeta();
        }
        catch
        {
            return new EpisodicMeta();
        }
    }

    private static void WriteMeta(string path, EpisodicMeta meta) =>
        File.WriteAllText(path, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

    private static string? ResolveEpisodicDir()
    {
        foreach (var p in new[]
                 {
                     @"\\wsl$\Ubuntu\home\busin\.hermes\episodic",
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         ".hermes",
                         "episodic"),
                 })
        {
            try
            {
                Directory.CreateDirectory(p);
                return p;
            }
            catch
            {
                // next
            }
        }

        return null;
    }

    private static string SanitizeFilePart(string name)
    {
        var s = Regex.Replace(name ?? string.Empty, @"[^\w\-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "item" : s.Length > 48 ? s[..48] : s;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    private static string ReadStr(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) ? el.GetString() ?? string.Empty : string.Empty;

    private sealed record PatternHit(
        string What,
        string ActualFirst,
        string CorrectedTo,
        int Count,
        HashSet<string> Sessions);

    private sealed class EpisodicMeta
    {
        public int SessionsSinceBatch { get; set; }
        public string? LastBatchUtc { get; set; }
    }
}
