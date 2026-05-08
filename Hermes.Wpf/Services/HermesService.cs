using System.Diagnostics;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class HermesService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogService _log;

    public HermesService(LogService logService)
    {
        _log = logService;
    }

    public event Action<string>? OutputReceived;

    public async Task<HermesExecutionResult> SendMessageAsync(
        string message,
        string wslWorkDir,
        HermesSettings settings,
        int timeoutSeconds = 180)
    {
        // WSL/bash treat \r as part of tokens; normalize to LF so tools never see `$'...\r'`.
        message = (message ?? string.Empty).ReplaceLineEndings("\n");
        // Hermes may pass fragments through nested shells; ASCII backtick triggers command substitution in bash.
        message = message.Replace('`', '\uFF40'); // U+FF40 FULLWIDTH GRAVE ACCENT — same glyph role, not special to bash
        // Use single-quoted bash argument to avoid accidental command substitution when the prompt contains Markdown fences (```),
        // $(), backticks, etc. Double-quoted strings would still be vulnerable to subtle escape edge-cases.
        var sq = EscapeForSingleQuotedBash(message);
        var script = ComposeScript(settings, wslWorkDir,
            $"{ActivationLine(settings)} && {settings.HermesCommand} -z '{sq}' chat");

        MaybeLogDiagnosticScript(settings, script, "chat");
        return await ExecuteWslArgvAsync(BuildWslArgv(settings, script), timeoutSeconds).ConfigureAwait(false);
    }

    /// <param name="timeoutSeconds">Use at least ~120s for long gateway sessions (or match chat timeout).</param>
    public async Task<HermesExecutionResult> RunQuickActionAsync(
        string command,
        string wslWorkDir,
        HermesSettings settings,
        int timeoutSeconds = 120)
    {
        command = (command ?? string.Empty).ReplaceLineEndings("\n").Replace('`', '\uFF40');
        var script =
            ComposeScript(settings, wslWorkDir, $"{ActivationLine(settings)} && {settings.HermesCommand} {command}");

        MaybeLogDiagnosticScript(settings, script, $"quick:{command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "?"}");
        return await ExecuteWslArgvAsync(BuildWslArgv(settings, script), timeoutSeconds).ConfigureAwait(false);
    }

    private void MaybeLogDiagnosticScript(HermesSettings settings, string script, string kind)
    {
#if DEBUG
        const bool forceDebug = true;
#else
        const bool forceDebug = false;
#endif
        if (!forceDebug && !settings.DiagnosticLogHermesCommands)
        {
            return;
        }

        var sanitized = string.Equals(kind, "chat", StringComparison.Ordinal)
            ? SanitizeChatScriptForLog(script)
            : TruncateForLog(script, 320);

        _log.LogInfo($"[hermes] diag bash-lc ({kind}): {sanitized}");
    }

    /// <summary>Redacts the <c>-z "…"</c> prompt payload for logs (length only).</summary>
    private static string SanitizeChatScriptForLog(string script)
    {
        const string marker = "-z \"";
        var idx = script.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            return TruncateForLog(script, 400);
        }

        var contentStart = idx + marker.Length;
        var i = contentStart;
        while (i < script.Length)
        {
            if (script[i] == '\\' && i + 1 < script.Length)
            {
                i += 2;
                continue;
            }

            if (script[i] == '"')
            {
                var len = i - contentStart;
                return script[..contentStart] + $"<omitted len={len}>\"" + script[(i + 1)..];
            }

            i++;
        }

        return TruncateForLog(script, 400);
    }

    private static string TruncateForLog(string s, int maxLen)
    {
        if (s.Length <= maxLen)
        {
            return s;
        }

        return s[..maxLen] + "…";
    }

    private static string ComposeScript(HermesSettings settings, string wslWorkDir, string bashTail)
    {
        var wp = (wslWorkDir ?? string.Empty).Trim();
        var cdPrefix = string.IsNullOrEmpty(wp) ? string.Empty : $"cd {BashSingleQuotePosixPath(wp)} && ";
        return $"{cdPrefix}{bashTail}";
    }

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

    /// <summary>
    /// Escapes arbitrary content for a single-quoted bash string literal.
    /// </summary>
    private static string EscapeForSingleQuotedBash(string s) =>
        (s ?? string.Empty).Replace("'", "'\\''", StringComparison.Ordinal);

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

    private async Task<HermesExecutionResult> ExecuteWslArgvAsync(IReadOnlyList<string> argv, int timeoutSeconds)
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
        string? lastStderrRaw = null;

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

            lastStderrRaw = e.Data;
            var line = $"[stderr] {e.Data}";
            sb.AppendLine(line);
            OutputReceived?.Invoke(line);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        int exitCode;
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            exitCode = -1;
            lastStderrRaw ??= "Timed out waiting for Hermes (wsl/bash).";
        }

        var combined = sb.ToString().Trim();
        if (exitCode != 0 && !string.IsNullOrEmpty(combined))
        {
            OutputReceived?.Invoke($"[exit {exitCode}]");
        }

        return new HermesExecutionResult
        {
            ExitCode = exitCode,
            CombinedText = combined,
            LastStderrLine = lastStderrRaw
        };
    }
}
