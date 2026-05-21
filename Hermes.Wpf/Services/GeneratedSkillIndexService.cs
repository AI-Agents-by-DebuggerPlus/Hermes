using System.IO;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Publishes skills/index.json for Hermes.Wpf and WSL ~/.hermes/skills consumers.</summary>
public sealed class GeneratedSkillIndexService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly LogService _log;

    public GeneratedSkillIndexService(LogService log) => _log = log;

    public void WriteIndex(HermesSettings settings, IReadOnlyList<GeneratedSkillManifest> skills)
    {
        var winRoot = GeneratedSkillPaths.ResolveWindowsSkillsRoot(settings);
        Directory.CreateDirectory(winRoot);

        var payload = new
        {
            version = 1,
            updatedAt = DateTime.UtcNow.ToString("O"),
            publisher = "Hermes.Wpf",
            skills = skills.Select(s => new
            {
                s.Id,
                s.Title,
                s.Summary,
                s.Kind,
                s.Enabled,
                s.Triggers,
                path = s.DirectoryPath,
                wslPath = settings.SkillMirrorToWslHermes
                    ? $"~/.hermes/skills/{s.Id}"
                    : string.Empty,
            }).ToList(),
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var winIndex = GeneratedSkillPaths.IndexPath(winRoot);
        File.WriteAllText(winIndex, json);
        _log.LogInfo($"[skill-index] wrote {skills.Count} skill(s) → {winIndex}");

        var wslRoot = GeneratedSkillPaths.ResolveWslSkillsRoot(settings);
        if (wslRoot is null)
        {
            return;
        }

        Directory.CreateDirectory(wslRoot);
        var wslIndex = GeneratedSkillPaths.IndexPath(wslRoot);
        File.WriteAllText(wslIndex, json);
        _log.LogInfo($"[skill-index] mirrored index → {wslIndex}");
    }
}
