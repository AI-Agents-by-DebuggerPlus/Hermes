using System.Diagnostics;
using System.IO;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class GeneratedSkillRunner
{
    private readonly LogService _log;

    public GeneratedSkillRunner(LogService log) => _log = log;

    public async Task<(bool Ok, string Detail)> RunAsync(GeneratedSkillManifest skill, CancellationToken cancellationToken = default)
    {
        if (!skill.Enabled)
        {
            return (false, "навык отключён");
        }

        if (!string.Equals(skill.Kind, "script", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"kind={skill.Kind} — не исполняемый script");
        }

        var scriptPath = ResolveScriptPath(skill);
        if (scriptPath is null || !File.Exists(scriptPath))
        {
            return (false, "файл скрипта не найден");
        }

        try
        {
            var (fileName, arguments) = BuildProcessStart(scriptPath);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = skill.DirectoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stdout.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.AppendLine(e.Data);
                }
            };

            if (!process.Start())
            {
                return (false, "Process.Start вернул false");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }
            });

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var outText = stdout.ToString().Trim();
            var errText = stderr.ToString().Trim();
            _log.LogInfo($"[skill-run] {skill.Id} exit={process.ExitCode}");

            if (outText.Length > 0)
            {
                _log.LogInfo($"[skill-run] stdout: {Truncate(outText, 2000)}");
            }

            if (errText.Length > 0)
            {
                _log.LogWarn($"[skill-run] stderr: {Truncate(errText, 2000)}");
            }

            if (process.ExitCode != 0)
            {
                var detail = errText.Length > 0 ? errText : $"exit {process.ExitCode}";
                return (false, Truncate(detail, 400));
            }

            var summary = outText.Length > 0 ? Truncate(outText, 400) : "exit 0";
            return (true, summary);
        }
        catch (Exception ex)
        {
            _log.LogError($"[skill-run] {skill.Id}: {ex.Message}");
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string Detail)> RunTestCommandAsync(string testCommand, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var cmd = (testCommand ?? string.Empty).Trim();
        if (cmd.Length == 0)
        {
            return (true, "no test");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "не удалось запустить test_command");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var detail = stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim();
                return (false, Truncate(detail, 400));
            }

            return (true, Truncate(stdout.Trim(), 200));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? ResolveScriptPath(GeneratedSkillManifest skill)
    {
        if (!string.IsNullOrWhiteSpace(skill.ScriptFile))
        {
            var p = Path.Combine(skill.DirectoryPath, skill.ScriptFile);
            if (File.Exists(p))
            {
                return p;
            }
        }

        foreach (var name in new[] { "run.ps1", "run.py" })
        {
            var p = Path.Combine(skill.DirectoryPath, name);
            if (File.Exists(p))
            {
                return p;
            }
        }

        return null;
    }

    private static (string FileName, string Arguments) BuildProcessStart(string scriptPath)
    {
        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        return ext switch
        {
            ".py" => ("python", $"\"{scriptPath}\""),
            _ => ("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\""),
        };
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
