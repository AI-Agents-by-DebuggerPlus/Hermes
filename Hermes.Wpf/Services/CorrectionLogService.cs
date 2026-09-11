using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Append-only hermes/corrections.jsonl (design §2.1 — code writes, not the model).</summary>
public sealed class CorrectionLogService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly LogService _log;
    private readonly object _gate = new();

    public CorrectionLogService(LogService log) => _log = log;

    public string GetLogPath(string projectWindowsPath) =>
        Path.Combine(HermesProjectLayout.GetHermesDirectory(projectWindowsPath), "corrections.jsonl");

    public void Append(string projectWindowsPath, CorrectionLogEntry entry)
    {
        var root = (projectWindowsPath ?? string.Empty).Trim();
        if (root.Length == 0)
        {
            return;
        }

        try
        {
            var hermesDir = HermesProjectLayout.GetHermesDirectory(root);
            Directory.CreateDirectory(hermesDir);
            var path = Path.Combine(hermesDir, "corrections.jsonl");
            var line = JsonSerializer.Serialize(entry, JsonOpts) + "\n";
            lock (_gate)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }

            _log.LogInfo(
                $"[corrections] {entry.Source} project={entry.Project} what={entry.What} → {path}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[corrections] append failed: {ex.Message}");
        }
    }
}
