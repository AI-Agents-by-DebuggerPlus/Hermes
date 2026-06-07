using System.IO;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Writes structured memory files into the External Brain vault.</summary>
public sealed class ExternalBrainWriteService
{
    private readonly LogService _log;
    private readonly MemoryExtractorService _extractor = new();

    public ExternalBrainWriteService(LogService log) => _log = log;

    /// <summary>Writes markdown under vault subfolder; returns full path or null.</summary>
    public string? TryWriteMemory(MemoryDraft draft, string vaultRoot, string? subfolderOverride = null)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot) || !Directory.Exists(vaultRoot))
        {
            return null;
        }

        if (!_extractor.ShouldSave(draft))
        {
            return null;
        }

        var sub = (subfolderOverride ?? MemoryExtractorService.MemorySubfolderForType(draft.Type))
            .Replace('/', Path.DirectorySeparatorChar);
        var dir = Path.Combine(vaultRoot, sub);
        Directory.CreateDirectory(dir);

        var fileName = MemoryExtractorService.BuildSaveFileName(draft.Type, draft.TimestampUtc);
        var path = Path.Combine(dir, fileName);
        var body = _extractor.GenerateMarkdown(draft);
        File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _log.LogInfo($"[external-brain-write] {path}");
        return path;
    }

    /// <summary>Copies screenshot PNG into vault; returns vault-relative path.</summary>
    public string? TryArchiveScreenshot(string vaultRoot, string? sourcePath, LocalAutomationKind kind)
    {
        if (string.IsNullOrWhiteSpace(vaultRoot) || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var relDir = kind switch
        {
            LocalAutomationKind.ReniWaterSubmit => Path.Combine("Knowledge", "Utilities", "ReniWater", "Screenshots"),
            _ => Path.Combine("Projects", "Utilities", "Screenshots"),
        };

        var targetDir = Path.Combine(vaultRoot, relDir);
        Directory.CreateDirectory(targetDir);

        var name = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Path.GetFileName(sourcePath)}";
        name = MemoryExtractorService.SanitizeFileName(name);
        var target = Path.Combine(targetDir, name);
        File.Copy(sourcePath, target, overwrite: true);
        _log.LogInfo($"[external-brain-write] screenshot → {target}");
        return Path.Combine(relDir, name).Replace('\\', '/');
    }
}
