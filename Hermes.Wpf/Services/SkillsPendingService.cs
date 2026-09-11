using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>skills_pending staging + auto-approve (design §2.3 / plan step 4).</summary>
internal sealed class SkillsPendingService
{
    public const int AutoApproveAfter = 2;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly LogService _log;

    public SkillsPendingService(LogService log) => _log = log;

    public string PendingDir(string projectWindowsPath) =>
        Path.Combine(HermesProjectLayout.GetHermesDirectory(projectWindowsPath), "skills_pending");

    public (string Applied, string Message) ProcessReflectionOutcome(
        string projectWindowsPath,
        ReflectionOutcomeParser.Outcome outcome)
    {
        return outcome.Action switch
        {
            "skill_draft" => ProcessSkillDraft(projectWindowsPath, outcome),
            "memory_fact" => ProcessMemoryFact(outcome),
            _ => ("ignored", $"Unknown action {outcome.Action}"),
        };
    }

    public bool TryApproveLatestPending(string projectWindowsPath, out string message)
    {
        message = string.Empty;
        var dir = PendingDir(projectWindowsPath);
        if (!Directory.Exists(dir))
        {
            message = "No pending skills.";
            return false;
        }

        var latest = Directory.GetFiles(dir, "*_skill_draft.json")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (latest is null)
        {
            message = "No pending skill_draft files.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(latest));
            var root = doc.RootElement;
            var name = root.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
            if (name.Length == 0 && root.TryGetProperty("name", out var n2))
            {
                name = n2.GetString() ?? "";
            }

            var content = root.TryGetProperty("Content", out var c) ? c.GetString() ?? "" : "";
            if (content.Length == 0 && root.TryGetProperty("content", out var c2))
            {
                content = c2.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
            {
                message = "Pending draft missing name/content.";
                return false;
            }

            var path = InstallSkill(name, content);
            File.Move(latest, latest + ".approved", overwrite: true);
            ResetStreak(projectWindowsPath);
            message = $"Approved skill «{SanitizeSlug(name)}» → {path}";
            _log.LogInfo($"[skills-pending] manual approve → {path}");
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private (string Applied, string Message) ProcessSkillDraft(
        string projectWindowsPath,
        ReflectionOutcomeParser.Outcome outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome.Name) || string.IsNullOrWhiteSpace(outcome.Content))
        {
            return ("rejected_empty_draft", "skill_draft requires name and content.");
        }

        var dir = PendingDir(projectWindowsPath);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var file = Path.Combine(dir, $"{stamp}_skill_draft.json");
        var fingerprint = Fingerprint(outcome.Name, outcome.Content);
        File.WriteAllText(
            file,
            JsonSerializer.Serialize(
                new
                {
                    outcome.Action,
                    outcome.Name,
                    outcome.Content,
                    fingerprint,
                    staged_at = DateTime.UtcNow.ToString("o"),
                },
                JsonOpts));

        var streakPath = Path.Combine(dir, "auto_approve_streak.json");
        var (count, prevFp) = ReadStreak(streakPath);
        if (string.Equals(prevFp, fingerprint, StringComparison.Ordinal))
        {
            count++;
        }
        else
        {
            count = 1;
            prevFp = fingerprint;
        }

        WriteStreak(streakPath, count, prevFp);

        if (count >= AutoApproveAfter)
        {
            var installed = InstallSkill(outcome.Name, outcome.Content);
            ResetStreak(projectWindowsPath);
            File.Move(file, file + ".auto_approved", overwrite: true);
            _log.LogInfo($"[skills-pending] auto-approve x{count} → {installed}");
            return (
                "skill_auto_approved",
                $"skill_draft auto-approved (×{count}): «{SanitizeSlug(outcome.Name)}» → {installed}");
        }

        return (
            "skill_staged",
            $"skill_draft staged ({count}/{AutoApproveAfter}). Same draft again → auto-approve, or say «approve pending skill».");
    }

    private (string Applied, string Message) ProcessMemoryFact(ReflectionOutcomeParser.Outcome outcome)
    {
        var text = (outcome.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return ("rejected_empty_fact", "memory_fact requires text.");
        }

        try
        {
            var memDir = ResolveHermesMemoriesDir();
            if (memDir is null)
            {
                return ("memory_dir_missing", "Could not resolve ~/.hermes/memories.");
            }

            // Pre-flight compact (same script as chat prelude).
            TryRunCompactor(memDir);

            var memoryPath = Path.Combine(memDir, "MEMORY.md");
            var existing = File.Exists(memoryPath) ? File.ReadAllText(memoryPath) : string.Empty;
            const int budget = 2200;
            var entry = text.Length > 400 ? text[..400] : text;
            var next = string.IsNullOrWhiteSpace(existing)
                ? entry + "\n"
                : existing.TrimEnd() + "\n§\n" + entry + "\n";
            if (next.Length > budget)
            {
                TryRunCompactor(memDir);
                existing = File.Exists(memoryPath) ? File.ReadAllText(memoryPath) : string.Empty;
                next = string.IsNullOrWhiteSpace(existing)
                    ? entry + "\n"
                    : existing.TrimEnd() + "\n§\n" + entry + "\n";
                if (next.Length > budget)
                {
                    return ("memory_full", "MEMORY.md still full after compact; fact not written.");
                }
            }

            File.WriteAllText(memoryPath, next, Encoding.UTF8);
            _log.LogInfo($"[skills-pending] memory_fact written ({entry.Length} chars)");
            return ("memory_written", "memory_fact written to MEMORY.md.");
        }
        catch (Exception ex)
        {
            return ("memory_error", ex.Message);
        }
    }

    private string InstallSkill(string name, string content)
    {
        var slug = SanitizeSlug(name);
        var destRoot = ResolveHermesSkillsDomainDir()
                       ?? Path.Combine(
                           Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                           ".hermes",
                           "skills",
                           "domain");
        var dest = Path.Combine(destRoot, slug);
        Directory.CreateDirectory(dest);
        var body = content.Contains("---", StringComparison.Ordinal)
            ? content.Trim() + "\n"
            : $"""
              ---
              name: {slug}
              description: >
                Auto-approved from reflection skill_draft.
              version: 1.0.0
              metadata:
                hermes:
                  tags: [auto, reflection]
              ---

              # {name.Trim()}

              {content.Trim()}

              """;
        var skillPath = Path.Combine(dest, "SKILL.md");
        File.WriteAllText(skillPath, body.Replace("\r\n", "\n"), new UTF8Encoding(false));
        return skillPath;
    }

    private static string SanitizeSlug(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "-").Trim('-');
        if (s.Length == 0)
        {
            s = "reflection-skill";
        }

        return s.Length > 48 ? s[..48].Trim('-') : s;
    }

    private static string Fingerprint(string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant() + "\n" + content.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private static (int Count, string Fingerprint) ReadStreak(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (0, string.Empty);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var count = root.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
            var fp = root.TryGetProperty("fingerprint", out var f) ? f.GetString() ?? "" : "";
            return (count, fp);
        }
        catch
        {
            return (0, string.Empty);
        }
    }

    private static void WriteStreak(string path, int count, string fingerprint) =>
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new { count, fingerprint, updated = DateTime.UtcNow.ToString("o") }, JsonOpts));

    private void ResetStreak(string projectWindowsPath)
    {
        var path = Path.Combine(PendingDir(projectWindowsPath), "auto_approve_streak.json");
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[skills-pending] reset streak: {ex.Message}");
        }
    }

    private static string? ResolveHermesMemoriesDir()
    {
        foreach (var p in new[]
                 {
                     @"\\wsl$\Ubuntu\home\busin\.hermes\memories",
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         ".hermes",
                         "memories"),
                 })
        {
            if (Directory.Exists(p))
            {
                return p;
            }
        }

        return null;
    }

    private static string? ResolveHermesSkillsDomainDir()
    {
        foreach (var p in new[]
                 {
                     @"\\wsl$\Ubuntu\home\busin\.hermes\skills\domain",
                     Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         ".hermes",
                         "skills",
                         "domain"),
                 })
        {
            try
            {
                Directory.CreateDirectory(p);
                return p;
            }
            catch
            {
                // try next
            }
        }

        return null;
    }

    private void TryRunCompactor(string memoryDir)
    {
        try
        {
            var script = FindCompactorScript();
            if (script is null)
            {
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{script}\" --memory-dir \"{memoryDir}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(20_000);
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[skills-pending] compact: {ex.Message}");
        }
    }

    private static string? FindCompactorScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "hermes", "memory_compactor.py");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
