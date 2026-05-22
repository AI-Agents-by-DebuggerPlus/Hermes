using System.Globalization;
using System.IO;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Exports platform memory/skills documentation into the External Brain vault for retrieval.</summary>
public sealed class HermesPlatformKnowledgeSyncService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const string RepoReportRelative = "Docs/Report/Experience_And_Skills_Logic_Report.md";

    private readonly LogService _log;

    public HermesPlatformKnowledgeSyncService(LogService log)
    {
        _log = log;
    }

    public HermesPlatformKnowledgeSyncResult TrySync(HermesSettings settings, ExternalBrainService brain)
    {
        var memoryRoot = (brain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (memoryRoot.Length == 0 || !Directory.Exists(memoryRoot))
        {
            return HermesPlatformKnowledgeSyncResult.Skipped("External Brain path not set");
        }

        var body = ResolveMarkdownBody();
        var stamp = ResolveSourceTimestampUtc();
        var markdown = BuildVaultMarkdown(body, stamp);
        var targetDir = Path.Combine(memoryRoot, "Knowledge", "Hermes");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, HermesPlatformKnowledgeInstructions.VaultFileName);

        if (!WriteIfChanged(targetPath, markdown))
        {
            return HermesPlatformKnowledgeSyncResult.Unchanged(memoryRoot);
        }

        brain.RestartWatcherAndReload("platform-knowledge-sync");
        _log.LogInfo($"[platform-knowledge] exported {HermesPlatformKnowledgeInstructions.VaultRelativePath}");
        return HermesPlatformKnowledgeSyncResult.Exported(memoryRoot, targetPath);
    }

    private static string ResolveMarkdownBody()
    {
        var repoPath = TryResolveRepoReportPath();
        if (repoPath is not null && File.Exists(repoPath))
        {
            var raw = File.ReadAllText(repoPath).ReplaceLineEndings("\n").Trim();
            if (raw.Length > 0)
            {
                return raw;
            }
        }

        return HermesPlatformKnowledgeInstructions.VaultMarkdownBody;
    }

    private static DateTime ResolveSourceTimestampUtc()
    {
        var repoPath = TryResolveRepoReportPath();
        if (repoPath is not null && File.Exists(repoPath))
        {
            return File.GetLastWriteTimeUtc(repoPath);
        }

        return DateTime.UtcNow;
    }

    private static string? TryResolveRepoReportPath()
    {
        var env = (Environment.GetEnvironmentVariable("HERMES_REPO_ROOT") ?? string.Empty).Trim();
        if (env.Length > 0)
        {
            var fromEnv = Path.Combine(env, RepoReportRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fromEnv))
            {
                return fromEnv;
            }
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, RepoReportRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            dir = parent?.FullName ?? string.Empty;
        }

        return null;
    }

    private static string BuildVaultMarkdown(string body, DateTime stampUtc)
    {
        var iso = stampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: semantic");
        sb.AppendLine("importance: 5");
        sb.AppendLine($"created: {iso}");
        sb.AppendLine($"updated: {iso}");
        sb.AppendLine("title: Hermes.Wpf — память, самообучение и навыки");
        sb.AppendLine("tags: [hermes, memory, skills, external-brain, skill-resolver, skill-save, platform, wpf]");
        sb.AppendLine("source: Hermes.Wpf platform-knowledge-sync");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(body.TrimEnd());
        if (!body.EndsWith('\n'))
        {
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("#hermes #memory #skills #external-brain #самообучение #навыки #опыт");
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static bool WriteIfChanged(string targetPath, string content)
    {
        if (File.Exists(targetPath))
        {
            var existing = File.ReadAllText(targetPath).ReplaceLineEndings("\n");
            if (existing == content)
            {
                return false;
            }
        }

        File.WriteAllText(targetPath, content, Utf8NoBom);
        return true;
    }
}

public readonly record struct HermesPlatformKnowledgeSyncResult(bool DidUpdate, string Detail)
{
    public static HermesPlatformKnowledgeSyncResult Skipped(string reason) => new(false, reason);

    public static HermesPlatformKnowledgeSyncResult Unchanged(string memoryRoot) =>
        new(false, $"unchanged ({memoryRoot})");

    public static HermesPlatformKnowledgeSyncResult Exported(string memoryRoot, string path) =>
        new(true, $"exported to {path} (vault {memoryRoot})");
}
