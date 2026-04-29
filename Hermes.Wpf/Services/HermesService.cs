using System.Diagnostics;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class HermesService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public event Action<string>? OutputReceived;

    public async Task<string> SendMessageAsync(string message, string wslWorkDir, HermesSettings settings, int timeoutSeconds = 60)
    {
        var escaped = message.Replace("'", "\\'").Replace("\"", "\\\"");
        var args = BuildWslArgs(
            settings,
            $"source {settings.VenvPath}/bin/activate && {settings.HermesCommand} chat -z '{escaped}'",
            wslWorkDir);

        return await ExecuteRawAsync(args, timeoutSeconds);
    }

    public async Task<string> RunQuickActionAsync(string command, string wslWorkDir, HermesSettings settings)
    {
        var args = BuildWslArgs(
            settings,
            $"source {settings.VenvPath}/bin/activate && {settings.HermesCommand} {command}",
            wslWorkDir);

        return await ExecuteRawAsync(args, 120);
    }

    /// <summary>
    /// Builds wsl.exe arguments that reliably find /bin/bash on any WSL distro.
    /// Never use -e bash because relay context may not have PATH configured.
    /// </summary>
    private static string BuildWslArgs(HermesSettings settings, string bashCommand, string? wslWorkDir = null)
    {
        var cdPrefix = string.IsNullOrWhiteSpace(wslWorkDir) ? string.Empty : $"cd '{wslWorkDir}' && ";
        var fullCmd = $"{cdPrefix}{bashCommand}";
        var escaped = fullCmd.Replace("\"", "\\\"");

        if (!string.IsNullOrWhiteSpace(settings.WslDistro))
        {
            return $"-d \"{settings.WslDistro}\" -- /bin/bash -lc \"{escaped}\"";
        }

        return $"-- /bin/bash -lc \"{escaped}\"";
    }

    private async Task<string> ExecuteRawAsync(string arguments, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        psi.Environment["WSL_UTF8"] = "1";

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

            var line = $"[stderr] {e.Data}";
            sb.AppendLine(line);
            OutputReceived?.Invoke(line);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        await process.WaitForExitAsync(cts.Token);
        return sb.ToString().Trim();
    }
}
