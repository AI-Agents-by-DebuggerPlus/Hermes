using System.Globalization;
using System.IO;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Export generated skill metadata into External Brain vault.</summary>
public sealed class GeneratedSkillVaultSyncService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogService _log;

    public GeneratedSkillVaultSyncService(LogService log) => _log = log;

    public bool TryExportSkill(ExternalBrainService brain, GeneratedSkillManifest skill, bool reloadBrain = true)
    {
        if (skill is null)
        {
            return false;
        }

        var memoryRoot = (brain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (memoryRoot.Length == 0 || !Directory.Exists(memoryRoot))
        {
            return false;
        }

        try
        {
            var targetDir = Path.Combine(memoryRoot, "Procedures", "GeneratedSkills");
            Directory.CreateDirectory(targetDir);
            var fileName = $"Skill_{SanitizeFileName(skill.Id)}.md";
            var targetPath = Path.Combine(targetDir, fileName);
            var markdown = BuildMarkdown(skill);
            var existing = File.Exists(targetPath) ? File.ReadAllText(targetPath, Utf8NoBom) : null;
            if (string.Equals(existing, markdown, StringComparison.Ordinal))
            {
                return false;
            }

            File.WriteAllText(targetPath, markdown, Utf8NoBom);
            if (reloadBrain)
            {
                brain.RestartWatcherAndReload("generated-skill-export");
            }

            _log.LogInfo($"[skill-vault] exported {skill.Id} → {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[skill-vault] export failed: {ex.Message}");
            return false;
        }
    }

    public int SyncAll(ExternalBrainService brain, IReadOnlyList<GeneratedSkillManifest> skills)
    {
        if (skills.Count == 0)
        {
            return 0;
        }

        var written = 0;
        foreach (var skill in skills)
        {
            if (TryExportSkill(brain, skill, reloadBrain: false))
            {
                written++;
            }
        }

        if (written > 0)
        {
            brain.RestartWatcherAndReload("generated-skill-bulk");
            _log.LogInfo($"[skill-vault] bulk sync wrote {written} skill note(s)");
        }

        return written;
    }

    private static string BuildMarkdown(GeneratedSkillManifest skill)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: procedural");
        sb.AppendLine($"importance: 4");
        sb.AppendLine($"timestamp: {skill.CreatedAtUtc.ToLocalTime():yyyy-MM-ddTHH:mm:ss}");
        sb.AppendLine("tags: [generated-skill, hermes-wpf, skill-gen]");
        sb.AppendLine($"project: {skill.Id}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Generated skill: {skill.Title}");
        sb.AppendLine();
        sb.AppendLine($"**Id:** `{skill.Id}`  ");
        sb.AppendLine($"**Kind:** {skill.Kind}  ");
        sb.AppendLine($"**Enabled:** {skill.Enabled}  ");
        sb.AppendLine($"**Folder:** `{skill.DirectoryPath}`");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(skill.Summary))
        {
            sb.AppendLine(skill.Summary.Trim());
            sb.AppendLine();
        }

        if (skill.Triggers.Count > 0)
        {
            sb.AppendLine("## Triggers");
            foreach (var t in skill.Triggers)
            {
                sb.AppendLine($"- {t}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(skill.OutboundPromptBlock))
        {
            sb.AppendLine("## Outbound prompt block");
            sb.AppendLine(skill.OutboundPromptBlock.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(skill.SourceTurn))
        {
            sb.AppendLine("## Source turn (excerpt)");
            sb.AppendLine("```");
            sb.AppendLine(skill.SourceTurn.Trim());
            sb.AppendLine("```");
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static string SanitizeFileName(string id)
    {
        var chars = id.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var s = new string(chars).Trim('_');
        return s.Length == 0 ? "skill" : s;
    }
}
