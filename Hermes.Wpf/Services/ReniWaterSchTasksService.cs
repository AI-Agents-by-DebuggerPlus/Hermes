using System.Diagnostics;
using System.IO;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Windows Task Scheduler helpers for Reni Water — invoked only when Hermes CLI requests via wpf_local
/// (so CLI tracks tool usage; WPF never schedules autonomously).
/// </summary>
public sealed class ReniWaterSchTasksService
{
    public const string MonthlyTaskName = "Hermes_ReniWater_MonthlySubmit";
    public const string HourlyTaskName = "Hermes_ReniWater_HourlyNotify";

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public ReniWaterSchTasksService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public async Task<(bool Ok, string Detail)> RegisterDefaultTasksAsync(CancellationToken cancellationToken = default)
    {
        var script = ResolveRegisterScriptPath();
        if (script is null)
        {
            return (false, "register_scheduled_tasks.ps1 не найден.");
        }

        _log.LogInfo($"[reni-water] schtasks register via {script}");
        var (code, output) = await RunPowerShellFileAsync(script, cancellationToken).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(output) ? $"exit {code}" : output.Trim();
        return (code == 0, detail);
    }

    public (bool Ok, string Detail) UnregisterTasks()
    {
        _log.LogInfo("[reni-water] schtasks unregister");
        var sb = new StringBuilder();
        var ok = true;
        foreach (var name in new[] { MonthlyTaskName, HourlyTaskName })
        {
            var (code, output) = RunProcess("schtasks", $"/Delete /TN \"{name}\" /F");
            if (code != 0 && !output.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("не существует", StringComparison.OrdinalIgnoreCase))
            {
                ok = false;
            }

            sb.AppendLine($"{name}: {(code == 0 ? "удалена" : output.Trim())}");
        }

        ClearDeprecatedInAppScheduleFields();
        return (ok, sb.ToString().Trim());
    }

    public string DescribeTasksStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Task Scheduler (только если Hermes CLI зарегистрировал задачи):");
        foreach (var name in new[] { MonthlyTaskName, HourlyTaskName })
        {
            var (_, output) = RunProcess("schtasks", $"/Query /TN \"{name}\" /FO LIST /V");
            if (string.IsNullOrWhiteSpace(output)
                || output.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || output.Contains("не найден", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"- {name}: не зарегистрирована");
            }
            else
            {
                var next = ExtractSchTaskLine(output, "Next Run Time") ?? ExtractSchTaskLine(output, "Следующий запуск");
                var status = ExtractSchTaskLine(output, "Status") ?? ExtractSchTaskLine(output, "Статус");
                sb.AppendLine($"- {name}: {(status ?? "зарегистрирована")}{(next is not null ? $", next={next}" : string.Empty)}");
            }
        }

        var legacy = DescribeDeprecatedInAppSchedule();
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            sb.AppendLine();
            sb.AppendLine(legacy);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Clears legacy in-app schedule fields (WPF no longer runs timers).</summary>
    public void ClearDeprecatedInAppScheduleFields()
    {
        var s = _settings();
        if (string.IsNullOrWhiteSpace(s.ReniWaterScheduleKind) && string.IsNullOrWhiteSpace(s.ReniWaterNextRunLocal))
        {
            return;
        }

        s.ReniWaterScheduleKind = string.Empty;
        s.ReniWaterNextRunLocal = null;
        _log.LogInfo("[reni-water] cleared deprecated in-app schedule fields");
    }

    private string? DescribeDeprecatedInAppSchedule()
    {
        var s = _settings();
        var kind = (s.ReniWaterScheduleKind ?? string.Empty).Trim();
        if (kind.Length == 0)
        {
            return null;
        }

        return "⚠ Устаревшее in-app расписание Hermes.Wpf отключено. "
               + "Настройте через Hermes CLI (schtasks / wpf_local reni_water_schtasks_register).";
    }

    private string? ResolveRegisterScriptPath()
    {
        var configured = (_settings().ReniWaterScriptDirectory ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            var p = Path.Combine(configured, "register_scheduled_tasks.ps1");
            if (File.Exists(p))
            {
                return p;
            }
        }

        var repo = Path.Combine(
            @"D:\Programming\AI_Agents\Hermes\scripts\reni_water",
            "register_scheduled_tasks.ps1");
        return File.Exists(repo) ? repo : null;
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellFileAsync(
        string scriptPath,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var combined = (stdout + "\n" + stderr).Trim();
        return (process.ExitCode, combined);
    }

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, (stdout + "\n" + stderr).Trim());
    }

    private static string? ExtractSchTaskLine(string output, string keyPrefix)
    {
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var idx = t.IndexOf(':');
                return idx >= 0 ? t[(idx + 1)..].Trim() : t;
            }
        }

        return null;
    }
}
