using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class EnglishTutorObsidianExporter
{
    private readonly LogService _log;

    public EnglishTutorObsidianExporter(LogService log)
    {
        _log = log;
    }

    public ExportResult ExportFromLocalStore(string memoryRootPath, string localStorePath)
    {
        var memRoot = (memoryRootPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(memRoot) || !Directory.Exists(memRoot))
        {
            return new ExportResult(false, $"Memory root not found: {memRoot}");
        }

        if (string.IsNullOrWhiteSpace(localStorePath) || !File.Exists(localStorePath))
        {
            return new ExportResult(false, $"Local vocab store not found: {localStorePath}");
        }

        StoreDiskRoot? root;
        try
        {
            var txt = File.ReadAllText(localStorePath);
            root = JsonSerializer.Deserialize<StoreDiskRoot>(
                txt,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            return new ExportResult(false, $"Failed to parse vocab store: {ex.Message}");
        }

        var lemmas = root?.Lemmas ?? [];
        var nowUtc = DateTime.UtcNow;

        var knowledgeRoot = Path.Combine(memRoot, "Knowledge", "EnglishTutor");
        var wordsDir = Path.Combine(knowledgeRoot, "words");
        Directory.CreateDirectory(wordsDir);

        var projectsRoot = Path.Combine(memRoot, "Projects", "EnglishTutor", "Sessions");
        Directory.CreateDirectory(projectsRoot);

        var identityRoot = Path.Combine(memRoot, "Identity");
        Directory.CreateDirectory(identityRoot);

        var profilePath = Path.Combine(identityRoot, "EnglishTutor_Profile.md");
        if (!File.Exists(profilePath))
        {
            File.WriteAllText(profilePath, BuildProfileStub(nowUtc), Utf8NoBom());
        }

        var mastered = lemmas.Count(l => string.Equals(l.Tier, "known", StringComparison.OrdinalIgnoreCase));
        var review = lemmas.Count(l => string.Equals(l.Tier, "review", StringComparison.OrdinalIgnoreCase));
        var learning = lemmas.Count(l => string.Equals(l.Tier, "learning", StringComparison.OrdinalIgnoreCase));

        var writtenWords = 0;
        foreach (var w in lemmas.Where(static z => !string.IsNullOrWhiteSpace(z.Lemma)))
        {
            var lemma = CanonicalLemma(w.Lemma);
            if (lemma.Length == 0)
            {
                continue;
            }

            var path = Path.Combine(wordsDir, SanitizeFileName(lemma) + ".md");
            File.WriteAllText(path, BuildWordMarkdown(w, nowUtc), Utf8NoBom());
            writtenWords++;
        }

        var indexPath = Path.Combine(knowledgeRoot, "vocabulary_index.md");
        File.WriteAllText(indexPath, BuildIndexMarkdown(mastered, review, learning, writtenWords, nowUtc), Utf8NoBom());

        // Write a lightweight session snapshot each export so progress is auditable.
        var sessionPath = Path.Combine(projectsRoot, $"{nowUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_export.md");
        File.WriteAllText(sessionPath, BuildExportSessionMarkdown(mastered, review, learning, writtenWords, nowUtc), Utf8NoBom());

        _log.LogInfo($"[english-tutor] exported vocab to Obsidian: words={writtenWords} index={indexPath}");
        return new ExportResult(true, $"Exported: words={writtenWords}, index={indexPath}, session={sessionPath}");
    }

    public static string LocalStorePath()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HermesWpf");
        return Path.Combine(root, "english_tutor_vocab.json");
    }

    private static string BuildIndexMarkdown(int mastered, int review, int learning, int total, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: semantic");
        sb.AppendLine($"timestamp: {nowUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("tags: [\"english\", \"tutor\", \"vocab\", \"index\"]");
        sb.AppendLine("importance: 3");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# English Tutor — vocabulary index");
        sb.AppendLine();
        sb.AppendLine($"Updated: {nowUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"- Total words: **{total}**");
        sb.AppendLine($"- Mastered (known): **{mastered}**");
        sb.AppendLine($"- Needs review: **{review}**");
        sb.AppendLine($"- Learning: **{learning}**");
        sb.AppendLine();
        sb.AppendLine("Folder: `words/` (one file per lemma)");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildExportSessionMarkdown(int mastered, int review, int learning, int total, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: episodic");
        sb.AppendLine($"timestamp: {nowUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("tags: [\"english\", \"tutor\", \"session\", \"export\"]");
        sb.AppendLine("importance: 2");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# English Tutor — export snapshot");
        sb.AppendLine();
        sb.AppendLine($"Time: {nowUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Progress: total={total}, known={mastered}, review={review}, learning={learning}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildProfileStub(DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: identity");
        sb.AppendLine($"timestamp: {nowUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("tags: [\"english\", \"tutor\", \"profile\"]");
        sb.AppendLine("importance: 4");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("# English Tutor — profile");
        sb.AppendLine();
        sb.AppendLine("## Goal");
        sb.AppendLine("- …");
        sb.AppendLine();
        sb.AppendLine("## Current level (approx)");
        sb.AppendLine("- …");
        sb.AppendLine();
        sb.AppendLine("## Preferences");
        sb.AppendLine("- Session length: …");
        sb.AppendLine("- Focus: vocab / grammar / speaking / listening");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildWordMarkdown(EnglishTutorWordState w, DateTime nowUtc)
    {
        var lemma = CanonicalLemma(w.Lemma);
        var tier = CanonicalTier(w.Tier);
        var exp = Math.Max(0, w.ExposureCount);
        var streak = Math.Max(0, w.SuccessStreak);
        var last = w.LastUtc ?? nowUtc;

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: semantic");
        sb.AppendLine($"timestamp: {last:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("tags: [\"english\", \"tutor\", \"vocab\", \"word\"]");
        sb.AppendLine("project: EnglishTutor");
        sb.AppendLine("importance: 3");
        sb.AppendLine($"tier: {tier}");
        sb.AppendLine($"exposures: {exp.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"success_streak: {streak.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"last_seen: {last:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {lemma}");
        sb.AppendLine();
        sb.AppendLine("## Meaning (RU)");
        sb.AppendLine("- …");
        sb.AppendLine();
        sb.AppendLine("## Examples");
        sb.AppendLine("- …");
        sb.AppendLine();
        sb.AppendLine("## Notes / mistakes");
        sb.AppendLine("- …");
        sb.AppendLine();
        return sb.ToString();
    }

    private static UTF8Encoding Utf8NoBom() => new(encoderShouldEmitUTF8Identifier: false);

    private static string CanonicalLemma(string s) =>
        string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();

    private static string CanonicalTier(string? t)
    {
        var x = (t ?? string.Empty).Trim().ToLowerInvariant();
        return x switch
        {
            "known" => "known",
            "review" => "review",
            "learning" => "learning",
            _ => "learning",
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.ToString().Trim().Length == 0 ? "word" : sb.ToString().Trim();
    }

    private sealed class StoreDiskRoot
    {
        public List<EnglishTutorWordState>? Lemmas { get; set; }
    }

    public readonly record struct ExportResult(bool Success, string Message);
}

