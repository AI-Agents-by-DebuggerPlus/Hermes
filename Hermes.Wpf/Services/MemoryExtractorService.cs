using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Heuristic extraction of learnable Markdown memory from chat turns.</summary>
public sealed class MemoryExtractorService
{
    private static readonly Regex ListLike = new(
        @"(\n\s*\d+[\.\)]\s+|шаг\s+\d|step\s+\d|^\s*\d+[\.\)]\s+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Explainish = new(
        @"\b(what\s+is|что\s+такое|how\s+does|как\s+работает|explain|объясни|definition|определен)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Failureish = new(
        @"\b(error|ошибк|exception|failed|не\s+удалось|timed?\s+out|timeout)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public MemoryDraft ExtractExperience(string task, string result)
    {
        var taskT = task?.Trim() ?? string.Empty;
        var resultT = result?.Trim() ?? string.Empty;
        var kind = Classify(taskT, resultT);
        var importance = ScoreImportance(taskT, resultT, kind);

        var titleLine = TitleFromTask(taskT);
        var reusable = SummarizeForReusable(resultT, taskT);

        var draft = new MemoryDraft
        {
            Type = kind,
            Problem = taskT,
            Solution = resultT,
            Reusable = reusable,
            Tags = InferTags(kind, taskT, resultT),
            Project = string.Empty,
            Importance = importance,
            TimestampUtc = DateTime.UtcNow,
        };

        return draft;
    }

    public bool ShouldSave(MemoryDraft draft)
    {
        if (draft is null)
        {
            return false;
        }

        var total = draft.Problem.Length + draft.Solution.Length;
        if (total < 24)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(draft.Solution) && string.IsNullOrWhiteSpace(draft.Problem))
        {
            return false;
        }

        return true;
    }

    public string GenerateMarkdown(MemoryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var type = NormalizeType(draft.Type);
        var imp = Math.Clamp(draft.Importance, 1, 5);
        var stamp = draft.TimestampUtc == default ? DateTime.UtcNow : draft.TimestampUtc;
        var iso = XmlRoundTripUtc(stamp);
        var tags = draft.Tags
            .Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tagsJson = JsonSerializer.Serialize(tags);

        var project = EscapeYamlScalar(draft.Project ?? string.Empty);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append("type: ").AppendLine(type);
        sb.Append("timestamp: ").AppendLine(iso);
        sb.Append("tags: ").AppendLine(tagsJson);
        sb.Append("project: ").AppendLine(project);
        sb.Append("importance: ").AppendLine(imp.ToString(NumberFormatInfo.InvariantInfo));
        sb.AppendLine("---");
        sb.AppendLine();

        var title = EscapeMarkdownHeading(TextOrPlaceholder(draft.Problem.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(), "Memory"));
        sb.Append("# ").AppendLine(title);
        sb.AppendLine();

        switch (type)
        {
            case "semantic":
                sb.AppendLine("## Concept");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Solution) ? draft.Problem : draft.Solution);
                break;
            case "episodic":
                sb.AppendLine("## What was done");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Problem) ? "(no user message)" : draft.Problem);
                sb.AppendLine();
                sb.AppendLine("## Result");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Solution) ? "(empty)" : draft.Solution);
                sb.AppendLine();
                sb.AppendLine("## Notes");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Reusable) ? "—" : draft.Reusable);
                break;
            default:
                sb.AppendLine("## Problem");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Problem) ? "—" : draft.Problem);
                sb.AppendLine();
                sb.AppendLine("## Solution");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Solution) ? "—" : draft.Solution);
                sb.AppendLine();
                sb.AppendLine("## Reusable");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(draft.Reusable) ? "—" : draft.Reusable);
                break;
        }

        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Returns target subdirectory name under Memory root (<c>Procedures</c>, <c>Projects</c>, <c>Knowledge</c>).</summary>
    public static string MemorySubfolderForType(string type)
    {
        return NormalizeType(type) switch
        {
            "procedural" => "Procedures",
            "semantic" => "Knowledge",
            "episodic" => "Projects",
            "identity" => "Identity",
            _ => "Procedures",
        };
    }

    public static string BuildSaveFileName(string type, DateTime utc)
    {
        var t = NormalizeType(type);
        var local = utc.ToLocalTime();
        return $"{local:yyyy-MM-dd}_{local:HH-mm}_{t}.md";
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.Length == 0 ? "memory.md" : sb.ToString();
    }

    public MemoryDraft? TryExtractFromMessages(IReadOnlyList<ChatMessage> messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return null;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(messages[i].Role, "Hermes", StringComparison.Ordinal))
            {
                continue;
            }

            var assistant = messages[i].Text ?? string.Empty;
            for (var j = i - 1; j >= 0; j--)
            {
                if (!string.Equals(messages[j].Role, "User", StringComparison.Ordinal))
                {
                    continue;
                }

                return ExtractExperience(messages[j].Text ?? string.Empty, assistant);
            }

            return ExtractExperience(string.Empty, assistant);
        }

        return null;
    }

    /// <summary>Structured draft from a built-in local automation (Reni Water, etc.).</summary>
    public MemoryDraft ExtractFromLocalExecution(LocalExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new MemoryDraft
        {
            Type = record.Success ? "procedural" : "episodic",
            Problem = record.UserTask ?? string.Empty,
            Solution = record.AssistantSummary ?? string.Empty,
            Reusable = record.AssistantSummary ?? string.Empty,
            Tags = InferLocalTags(record),
            Project = record.ProjectName ?? string.Empty,
            Importance = record.Success ? 5 : 4,
            TimestampUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Extract, write to vault, return draft for role capture.</summary>
    public async Task<(MemoryDraft? Draft, string? VaultPath)> ExtractAndSaveAsync(
        MemoryDraft draft,
        string vaultRoot,
        ExternalBrainWriteService writer,
        string? subfolderOverride = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        if (!ShouldSave(draft))
        {
            return (null, null);
        }

        var path = writer.TryWriteMemory(draft, vaultRoot, subfolderOverride);
        return (draft, path);
    }

    private static List<string> InferLocalTags(LocalExecutionRecord record)
    {
        var tags = new List<string> { "hermes", "local-automation" };
        switch (record.Kind)
        {
            case LocalAutomationKind.ReniWaterSubmit:
            case LocalAutomationKind.ReniWaterAck:
            case LocalAutomationKind.ReniWaterSchedule:
            case LocalAutomationKind.ReniWaterSessionCheck:
                tags.AddRange(["utilities", "reni", "vodokanal", "reni_water"]);
                break;
        }

        tags.Add(record.Success ? "success" : "failure");
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Classify(string task, string result)
    {
        if (Explainish.IsMatch(task))
        {
            return "semantic";
        }

        if (Failureish.IsMatch(result))
        {
            return "episodic";
        }

        if (ListLike.IsMatch(result) || result.Length > 380)
        {
            return "procedural";
        }

        return result.Length > 120 ? "procedural" : "episodic";
    }

    private static int ScoreImportance(string task, string result, string kind)
    {
        var len = Math.Max(task.Length, result.Length);
        var n = 3;
        if (len > 2000)
        {
            n++;
        }

        if (len > 6000)
        {
            n++;
        }

        if (string.Equals(kind, "procedural", StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }

        return Math.Clamp(n, 1, 5);
    }

    private static List<string> InferTags(string kind, string task, string result)
    {
        var tags = new List<string> { "hermes", "chat" };
        tags.Add(kind);
        var blob = (task + " " + result).ToLowerInvariant();
        if (blob.Contains("wpf", StringComparison.Ordinal) || blob.Contains(".net", StringComparison.Ordinal))
        {
            tags.Add("dotnet");
        }

        if (blob.Contains("supabase", StringComparison.Ordinal))
        {
            tags.Add("supabase");
        }

        return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string TitleFromTask(string task)
    {
        var line = task.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(line))
        {
            return "Experience";
        }

        return line.Length > 80 ? line[..80] + "…" : line;
    }

    private static string SummarizeForReusable(string result, string task)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return task.Length > 200 ? task[..200] + "…" : task;
        }

        var first = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? result;
        return first.Length > 320 ? first[..320] + "…" : first;
    }

    private static string TextOrPlaceholder(string? s, string fallback) =>
        string.IsNullOrWhiteSpace(s) ? fallback : s!;

    private static string EscapeMarkdownHeading(string s) => s.Replace("\r", string.Empty).Replace("\n", " ").Trim();

    private static string EscapeYamlScalar(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Replace("\r", string.Empty).Replace("\n", " ").Trim();
    }

    private static string XmlRoundTripUtc(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Unspecified)
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        if (utc.Kind == DateTimeKind.Local)
        {
            utc = utc.ToUniversalTime();
        }

        return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    private static string NormalizeType(string? type)
    {
        var t = (type ?? "procedural").Trim().ToLowerInvariant();
        return t switch
        {
            "procedural" or "semantic" or "episodic" or "identity" => t,
            _ => "procedural",
        };
    }
}
