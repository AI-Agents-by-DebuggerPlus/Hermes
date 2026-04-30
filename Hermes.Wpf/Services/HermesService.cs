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
        // Double-quote the prompt — single-quoted -z '…' breaks for many non-ASCII prompts on Ubuntu/WSL.
        var dq = EscapeForDoubleQuotedBash(message);

        // Global -z must appear before the subcommand name (see `hermes --help`: [-z PROMPT] … {chat,…}).
        var script = ComposeScript(settings, wslWorkDir,
            $"{ActivationLine(settings)} && {settings.HermesCommand} -z \"{dq}\" chat");

        return await ExecuteWslArgvAsync(BuildWslArgv(settings, script), timeoutSeconds).ConfigureAwait(false);
    }

    public async Task<string> RunQuickActionAsync(string command, string wslWorkDir, HermesSettings settings)
    {
        var script =
            ComposeScript(settings, wslWorkDir, $"{ActivationLine(settings)} && {settings.HermesCommand} {command}");

        return await ExecuteWslArgvAsync(BuildWslArgv(settings, script), 120).ConfigureAwait(false);
    }

    private static string ComposeScript(HermesSettings settings, string wslWorkDir, string bashTail)
    {
        var wp = (wslWorkDir ?? string.Empty).Trim();
        var cdPrefix = string.IsNullOrEmpty(wp) ? string.Empty : $"cd {BashSingleQuotePosixPath(wp)} && ";
        return $"{cdPrefix}{bashTail}";
    }

    /// <summary>source …/bin/activate — mirrors ConnectionService tilde rules (~/ must not sit only inside double quotes alone).</summary>
    private static string ActivationLine(HermesSettings settings)
    {
        var vp = settings.VenvPath.Trim();
        if (string.IsNullOrEmpty(vp))
        {
            return "true";
        }

        if (vp.StartsWith("~/", StringComparison.Ordinal))
        {
            var rest = vp[2..];
            return $"source \"$HOME/{rest}/bin/activate\"";
        }

        if (vp.StartsWith("/", StringComparison.Ordinal))
        {
            return $"source {BashSingleQuotePosixPath(vp)}/bin/activate";
        }

        return $"source \"{EscapeInnerDouble(vp)}/bin/activate\"";
    }

    private static string BashSingleQuotePosixPath(string unixPath) =>
        "'" + unixPath.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string EscapeInnerDouble(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeForDoubleQuotedBash(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`")
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("$", "\\$");

    private static IReadOnlyList<string> BuildWslArgv(HermesSettings settings, string bashLcScriptOneArgument)
    {
        var argv = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.WslDistro))
        {
            argv.Add("-d");
            argv.Add(settings.WslDistro.Trim());
        }

        argv.Add("--");
        argv.Add("/bin/bash");
        argv.Add("-lc");
        argv.Add(bashLcScriptOneArgument);

        return argv;
    }

    private async Task<string> ExecuteWslArgvAsync(IReadOnlyList<string> argv, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        psi.Environment["WSL_UTF8"] = "1";

        psi.ArgumentList.Clear();
        foreach (var chunk in argv)
        {
            psi.ArgumentList.Add(chunk);
        }

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
        await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        var text = sb.ToString().Trim();
        if (process.ExitCode != 0 && !string.IsNullOrEmpty(text))
        {
            OutputReceived?.Invoke($"[exit {process.ExitCode}]");
        }

        return text;
    }
}
