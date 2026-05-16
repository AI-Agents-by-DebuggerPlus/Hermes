using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class ReniWaterScriptService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex ScreenshotLineRegex = new(
        @"^\s*Screenshot:\s*(.+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public ReniWaterScriptService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public event Action<string>? OutputReceived;

    public string? ResolveScriptDirectory()
    {
        var settings = _settings();
        var configured = (settings.ReniWaterScriptDirectory ?? string.Empty).Trim();
        if (IsValidScriptDir(configured))
        {
            return configured;
        }

        var ws = (settings.WorkspaceRootWindowsPath ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(ws))
        {
            var underWs = Path.Combine(ws, "scripts", "reni_water");
            if (IsValidScriptDir(underWs))
            {
                return underWs;
            }
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "reni_water");
            if (IsValidScriptDir(candidate))
            {
                return candidate;
            }
        }

        const string devFallback = @"D:\Programming\AI_Agents\Hermes\scripts\reni_water";
        return IsValidScriptDir(devFallback) ? devFallback : null;
    }

    public string ResolvePendingAckPath()
    {
        var configured = (_settings().ReniWaterPendingAckPath ?? string.Empty).Trim();
        return string.IsNullOrEmpty(configured)
            ? @"d:\Documents\Utilities\water\pending_ack.json"
            : configured;
    }

    public ReniWaterPendingInfo? ReadPendingAck()
    {
        var path = ResolvePendingAckPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            return new ReniWaterPendingInfo
            {
                ReadingSubmitted = root.TryGetProperty("reading_submitted", out var r)
                    ? r.GetString() ?? "?"
                    : "?",
                ScreenshotPath = root.TryGetProperty("screenshot", out var s)
                    ? s.GetString() ?? string.Empty
                    : string.Empty,
                CreatedUtc = root.TryGetProperty("created_utc", out var c)
                    ? c.GetString() ?? string.Empty
                    : string.Empty,
                AuthRequired = root.TryGetProperty("auth_required", out var a) && a.GetBoolean(),
                Message = root.TryGetProperty("message_uk", out var m)
                    ? m.GetString() ?? string.Empty
                    : string.Empty,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[reni-water] pending_ack parse failed: {ex.Message}");
            return null;
        }
    }

    public void OpenLoginConsole()
    {
        var scriptDir = ResolveScriptDirectory()
                        ?? throw new InvalidOperationException("Скрипт run_submit.ps1 не найден. Укажите ReniWaterScriptDirectory в settings.json.");
        var script = Path.Combine(scriptDir, "run_submit.ps1");

        var args =
            $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{script}\" -login";
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = args,
            WorkingDirectory = scriptDir,
            UseShellExecute = true,
        });
        _log.LogInfo("[reni-water] opened interactive PowerShell for -login");
    }

    public Task<ReniWaterRunResult> RunSubmitAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(extraArgs: [], timeoutSeconds: 180, cancellationToken);

    public Task<ReniWaterRunResult> RunAckAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(extraArgs: ["--ack"], timeoutSeconds: 60, cancellationToken);

    public Task<ReniWaterRunResult> RunCheckSessionAsync(CancellationToken cancellationToken = default) =>
        RunScriptAsync(extraArgs: ["--check-session"], timeoutSeconds: 90, cancellationToken);

    private async Task<ReniWaterRunResult> RunScriptAsync(
        IReadOnlyList<string> extraArgs,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var scriptDir = ResolveScriptDirectory()
                        ?? throw new InvalidOperationException(
                            "Не найден scripts\\reni_water\\run_submit.ps1. Задайте ReniWaterScriptDirectory в %AppData%\\HermesWpf\\settings.json.");
        var script = Path.Combine(scriptDir, "run_submit.ps1");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = scriptDir,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };

        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        foreach (var arg in extraArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        _log.LogInfo($"[reni-water] start: {script} {string.Join(' ', extraArgs)}");

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            sb.AppendLine(e.Data);
            OutputReceived?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            sb.AppendLine(e.Data);
            OutputReceived?.Invoke(e.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Не удалось запустить powershell.exe");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            throw new TimeoutException($"Таймаут {timeoutSeconds} с ({script})");
        }

        var text = sb.ToString().Trim();
        var authRequired = text.Contains("AUTH_REQUIRED", StringComparison.OrdinalIgnoreCase);
        var submitAccepted = text.Contains("SUBMIT_ACCEPTED", StringComparison.OrdinalIgnoreCase);
        var screenshot = ParseScreenshotPath(text) ?? ReadPendingAck()?.ScreenshotPath;
        _log.LogInfo(
            $"[reni-water] exit={process.ExitCode} auth={authRequired} accepted={submitAccepted} "
            + $"screenshot={screenshot ?? "(none)"}");

        return new ReniWaterRunResult
        {
            ExitCode = process.ExitCode,
            CombinedText = text,
            AuthRequired = authRequired,
            SubmitAccepted = submitAccepted,
            ScreenshotPath = screenshot,
        };
    }

    public static string? ParseScreenshotPath(string combinedOutput)
    {
        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return null;
        }

        foreach (var line in combinedOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = ScreenshotLineRegex.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var path = m.Groups[1].Value.Trim().Trim('"');
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    private static bool IsValidScriptDir(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(Path.Combine(path, "run_submit.ps1"))
        && File.Exists(Path.Combine(path, "submit_reni_water_reading.py"));
}
