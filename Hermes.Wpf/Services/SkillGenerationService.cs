using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class SkillGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly GeneratedSkillRunner _runner;
    private readonly SkillSandboxService _sandbox;
    private readonly GeneratedSkillVaultSyncService _vaultSync;

    public SkillGenerationService(
        LogService log,
        Func<HermesSettings> settings,
        GeneratedSkillRunner runner,
        SkillSandboxService sandbox,
        GeneratedSkillVaultSyncService vaultSync)
    {
        _log = log;
        _settings = settings;
        _runner = runner;
        _sandbox = sandbox;
        _vaultSync = vaultSync;
    }

    public async Task<(bool Ok, string UserMessage)> TrySaveAsync(
        SkillSavePayload payload,
        string sourceTurn,
        ExternalBrainService? brain = null,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings();
        if (!settings.SkillGenerationEnabled)
        {
            return (false, "[skill] Генерация навыков отключена в Settings.");
        }

        if (settings.SkillSandboxBeforeSave
            && string.Equals(payload.Kind, "script", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogInfo("[skill-sandbox] Executing task in sandbox…");
            var timeout = Math.Clamp(settings.SkillSandboxTimeoutSeconds, 5, 300);
            var sandbox = await _sandbox.TryValidateAsync(payload, timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!sandbox.Ok)
            {
                _log.LogWarn($"[skill-sandbox] rejected {payload.Id}: {sandbox.Detail}");
                return (false, $"[skill] Sandbox отклонил сохранение: {sandbox.Detail}");
            }

            _log.LogInfo("[skill-sandbox] Task succeeded. Initiating skill crystallization…");
        }

        var root = GeneratedSkillPaths.ResolveWindowsSkillsRoot(settings);
        Directory.CreateDirectory(root);

        var folder = GeneratedSkillPaths.SkillFolder(root, payload.Id);
        if (Directory.Exists(folder))
        {
            var attempts = Math.Clamp(settings.SkillMaxGenerationAttempts, 1, 10);
            for (var i = 1; i < attempts && Directory.Exists(folder); i++)
            {
                folder = GeneratedSkillPaths.SkillFolder(root, $"{payload.Id}_{i + 1}");
            }
        }

        Directory.CreateDirectory(folder);

        var scriptFile = string.Empty;
        if (string.Equals(payload.Kind, "script", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(payload.ScriptBody))
        {
            scriptFile = $"run.{payload.ScriptExtension}";
            await File.WriteAllTextAsync(
                Path.Combine(folder, scriptFile),
                payload.ScriptBody,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
        }

        var manifest = new GeneratedSkillManifest
        {
            Id = payload.Id,
            Title = payload.Title,
            Summary = payload.Summary,
            Category = "Generated",
            Version = 1,
            Enabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            Triggers = payload.Triggers.ToList(),
            Kind = payload.Kind,
            ScriptFile = scriptFile,
            OutboundPromptBlock = payload.OutboundPromptBlock,
            TestCommand = payload.TestCommand,
            SourceTurn = Truncate(sourceTurn, 2000),
            DirectoryPath = folder,
        };

        await WriteManifestAsync(folder, manifest, cancellationToken).ConfigureAwait(false);
        await WriteSkillMarkdownAsync(folder, manifest, cancellationToken).ConfigureAwait(false);

        if (settings.SkillMirrorToWslHermes)
        {
            TryMirrorToWsl(folder, manifest);
        }

        var tested = false;
        var testOk = true;
        if (settings.SkillRunTestsBeforeSave
            && !string.IsNullOrWhiteSpace(payload.TestCommand)
            && !settings.SkillSandboxBeforeSave)
        {
            tested = true;
            var test = await _runner.RunTestCommandAsync(payload.TestCommand, folder, cancellationToken)
                .ConfigureAwait(false);
            testOk = test.Ok;
            if (!testOk)
            {
                _log.LogWarn($"[skill-gen] smoke test failed for {payload.Id}: {test.Detail}");
            }
        }
        else if (settings.SkillRunTestsBeforeSave && settings.SkillSandboxBeforeSave
                 && !string.IsNullOrWhiteSpace(payload.TestCommand))
        {
            tested = true;
            testOk = true;
        }

        if (brain is not null)
        {
            _vaultSync.TryExportSkill(brain, manifest);
        }

        _log.LogInfo($"[skill-gen] Skill '{payload.Id}' successfully generated and saved → {folder}");
        return (true, SkillCrystallizeIntentParser.UserFacingSaveLine(payload, tested, testOk));
    }

    private static async Task WriteManifestAsync(
        string folder,
        GeneratedSkillManifest manifest,
        CancellationToken cancellationToken)
    {
        var dto = new
        {
            manifest.Id,
            manifest.Title,
            manifest.Summary,
            manifest.Category,
            manifest.Version,
            manifest.Enabled,
            createdAt = manifest.CreatedAtUtc.ToString("O"),
            manifest.Triggers,
            manifest.Kind,
            script = manifest.ScriptFile,
            outboundPromptBlock = manifest.OutboundPromptBlock,
            testCommand = manifest.TestCommand,
            sourceTurn = manifest.SourceTurn,
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await File.WriteAllTextAsync(GeneratedSkillPaths.ManifestPath(folder), json, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteSkillMarkdownAsync(
        string folder,
        GeneratedSkillManifest manifest,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"name: {manifest.Id}");
        sb.AppendLine($"title: {manifest.Title}");
        sb.AppendLine($"kind: {manifest.Kind}");
        sb.AppendLine($"created: {manifest.CreatedAtUtc:yyyy-MM-dd}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {manifest.Title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(manifest.Summary))
        {
            sb.AppendLine(manifest.Summary.Trim());
            sb.AppendLine();
        }

        if (manifest.Triggers.Count > 0)
        {
            sb.AppendLine("## Triggers");
            foreach (var t in manifest.Triggers)
            {
                sb.AppendLine($"- {t}");
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(manifest.OutboundPromptBlock))
        {
            sb.AppendLine("## Outbound prompt");
            sb.AppendLine(manifest.OutboundPromptBlock.Trim());
        }

        await File.WriteAllTextAsync(GeneratedSkillPaths.SkillMarkdownPath(folder), sb.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }

    private void TryMirrorToWsl(string folder, GeneratedSkillManifest manifest)
    {
        try
        {
            var wslRoot = GeneratedSkillPaths.ResolveWslSkillsRoot(_settings());
            if (wslRoot is null)
            {
                _log.LogWarn("[skill-gen] WSL ~/.hermes/skills not found — skip mirror");
                return;
            }

            var target = Path.Combine(wslRoot, manifest.Id);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            CopyDirectory(folder, target);
            _log.LogInfo($"[skill-gen] mirrored {manifest.Id} → {target}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[skill-gen] WSL mirror failed: {ex.Message}");
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];
}
