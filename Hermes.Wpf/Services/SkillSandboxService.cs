using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Isolated pre-save execution for generated script skills (temp folder, timeout).</summary>
public sealed class SkillSandboxService
{
    private static readonly Regex RiskyPattern = new(
        @"\b(Format-Volume|Clear-Disk|Remove-Item\s+-Recurse\s+.*\\|Invoke-Expression|iex\b|Start-Process\s+-Verb\s+RunAs|reg\s+delete|shutdown\s|Restart-Computer|Remove-Computer)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly LogService _log;
    private readonly GeneratedSkillRunner _runner;

    public SkillSandboxService(LogService log, GeneratedSkillRunner runner)
    {
        _log = log;
        _runner = runner;
    }

    public async Task<(bool Ok, string Detail)> TryValidateAsync(
        SkillSavePayload payload,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(payload.Kind, "script", StringComparison.OrdinalIgnoreCase))
        {
            return (true, "not a script");
        }

        if (string.IsNullOrWhiteSpace(payload.ScriptBody))
        {
            return (false, "script_body пустой");
        }

        if (RiskyPattern.IsMatch(payload.ScriptBody))
        {
            return (false, "скрипт содержит потенциально опасные команды — сохранение отклонено");
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "HermesSkillSandbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var scriptName = $"run.{payload.ScriptExtension.TrimStart('.')}";
            var scriptPath = Path.Combine(tempRoot, scriptName);
            await File.WriteAllTextAsync(scriptPath, payload.ScriptBody, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(payload.TestCommand))
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)));
                var test = await _runner.RunTestCommandAsync(payload.TestCommand, tempRoot, cts.Token)
                    .ConfigureAwait(false);
                _log.LogInfo($"[skill-sandbox] test_command ok={test.Ok}");
                return test;
            }

            var manifest = new GeneratedSkillManifest
            {
                Id = payload.Id,
                Kind = "script",
                ScriptFile = scriptName,
                DirectoryPath = tempRoot,
                Enabled = true,
            };

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300)));
            var run = await _runner.RunAsync(manifest, runCts.Token).ConfigureAwait(false);
            _log.LogInfo($"[skill-sandbox] dry-run ok={run.Ok}");
            return run;
        }
        catch (OperationCanceledException)
        {
            return (false, $"таймаут sandbox ({timeoutSeconds}s)");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // ignore temp cleanup
            }
        }
    }
}
