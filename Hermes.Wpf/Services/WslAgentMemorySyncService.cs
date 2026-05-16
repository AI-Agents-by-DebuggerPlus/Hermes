using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>One-way export of Hermes CLI memory files from WSL into the External Brain vault.</summary>
public sealed class WslAgentMemorySyncService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogService _log;

    public WslAgentMemorySyncService(LogService log)
    {
        _log = log;
    }

    public WslAgentMemorySyncResult TrySync(HermesSettings settings, ExternalBrainService brain)
    {
        if (!settings.SyncWslAgentMemoryToExternalBrain)
        {
            return WslAgentMemorySyncResult.Skipped("disabled");
        }

        var memoryRoot = (brain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (memoryRoot.Length == 0 || !Directory.Exists(memoryRoot))
        {
            return WslAgentMemorySyncResult.Skipped("External Brain path not set");
        }

        var wslMemoriesDir = WslAgentMemoryPaths.ResolveMemoriesDirectory(settings);
        if (wslMemoriesDir is null)
        {
            return WslAgentMemorySyncResult.Skipped("WSL memories folder not found");
        }

        var written = 0;
        var userPath = Path.Combine(wslMemoriesDir, "USER.md");
        if (File.Exists(userPath))
        {
            written += WriteSnapshot(
                memoryRoot,
                "Identity",
                "WslAgent_USER.md",
                "identity",
                "WSL agent user profile",
                userPath,
                ["wsl", "hermes-agent", "auto-sync", "user"]);
        }

        var memoryPath = Path.Combine(wslMemoriesDir, "MEMORY.md");
        if (File.Exists(memoryPath))
        {
            written += WriteSnapshot(
                memoryRoot,
                "Knowledge",
                "WslAgent_MEMORY.md",
                "semantic",
                "WSL agent long-term memory",
                memoryPath,
                ["wsl", "hermes-agent", "auto-sync", "memory"]);
        }

        if (written == 0)
        {
            return WslAgentMemorySyncResult.Unchanged(wslMemoriesDir);
        }

        brain.RestartWatcherAndReload("wsl-memory-sync");
        _log.LogInfo($"[wsl-memory-sync] exported {written} file(s) from {wslMemoriesDir} to {memoryRoot}");
        return WslAgentMemorySyncResult.Exported(written, wslMemoriesDir, memoryRoot);
    }

    private int WriteSnapshot(
        string memoryRoot,
        string subfolder,
        string fileName,
        string type,
        string title,
        string sourcePath,
        IReadOnlyList<string> tags)
    {
        var raw = File.ReadAllText(sourcePath).ReplaceLineEndings("\n").Trim();
        if (raw.Length == 0)
        {
            return 0;
        }

        var entries = WslAgentMemoryPaths.SplitEntries(raw);
        var stamp = File.GetLastWriteTimeUtc(sourcePath);
        var markdown = BuildVaultMarkdown(type, stamp, tags, title, entries, Path.GetFileName(sourcePath));
        var targetDir = Path.Combine(memoryRoot, subfolder);
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, fileName);
        return WriteIfChanged(targetPath, markdown) ? 1 : 0;
    }

    private static string BuildVaultMarkdown(
        string type,
        DateTime stampUtc,
        IReadOnlyList<string> tags,
        string title,
        IReadOnlyList<string> entries,
        string sourceFileName)
    {
        var iso = XmlRoundTripUtc(stampUtc);
        var tagsJson = JsonSerializer.Serialize(tags);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append("type: ").AppendLine(type);
        sb.Append("timestamp: ").AppendLine(iso);
        sb.Append("tags: ").AppendLine(tagsJson);
        sb.AppendLine("project: Hermes");
        sb.AppendLine("importance: 5");
        sb.Append("source: ").AppendLine($"wsl-memories/{sourceFileName}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# ").AppendLine(title);
        sb.AppendLine();

        if (entries.Count <= 1)
        {
            sb.AppendLine(entries.Count == 0 ? string.Empty : entries[0]);
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            sb.Append("## Entry ").Append(i + 1).AppendLine();
            sb.AppendLine();
            sb.AppendLine(entries[i]);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static bool WriteIfChanged(string path, string content)
    {
        var normalized = content.ReplaceLineEndings("\n");
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).ReplaceLineEndings("\n");
            if (string.Equals(existing, normalized, StringComparison.Ordinal))
            {
                return false;
            }
        }

        File.WriteAllText(path, normalized, Utf8NoBom);
        return true;
    }

    private static string XmlRoundTripUtc(DateTime utc) =>
        (utc == default ? DateTime.UtcNow : utc).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

public readonly record struct WslAgentMemorySyncResult(bool DidUpdate, int WrittenFiles, string Detail)
{
    public static WslAgentMemorySyncResult Skipped(string reason) => new(false, 0, reason);

    public static WslAgentMemorySyncResult Unchanged(string wslMemoriesDir) =>
        new(false, 0, $"unchanged ({wslMemoriesDir})");

    public static WslAgentMemorySyncResult Exported(int written, string wslMemoriesDir, string memoryRoot) =>
        new(true, written, $"wrote {written} file(s) from {wslMemoriesDir} to {memoryRoot}");
}
