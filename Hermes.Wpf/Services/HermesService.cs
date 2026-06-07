using System.Diagnostics;
using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class HermesService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogService _log;
    private readonly SemaphoreSlim _wslProcessGate = new(1, 1);
    private readonly object _wslHomeSync = new();
    private string? _cachedWslHomeDir;

    public HermesService(LogService logService)
    {
        _log = logService;
    }

    public event Action<string>? OutputReceived;

    public async Task<HermesExecutionResult> SendMessageAsync(
        string message,
        string wslWorkDir,
        HermesSettings settings,
        int timeoutSeconds = 180,
        string? resumeSessionId = null)
    {
        // WSL/bash treat \r as part of tokens; normalize to LF so tools never see `$'...\r'`.
        message = (message ?? string.Empty).ReplaceLineEndings("\n");
        // Hermes may pass fragments through nested shells; ASCII backtick triggers command substitution in bash.
        message = message.Replace('`', '\uFF40'); // U+FF40 FULLWIDTH GRAVE ACCENT — same glyph role, not special to bash
        var sq = EscapeForSingleQuotedBash(message);
        var resume = string.IsNullOrWhiteSpace(resumeSessionId)
            ? string.Empty
            : $" --resume {BashSingleQuotePosixPath(resumeSessionId.Trim())}";
        var tmpPrelude = BuildInlineToolTempPrelude(wslWorkDir);
        var script = ComposeScript(
            settings,
            wslWorkDir,
            $"{tmpPrelude}{ActivationLine(settings)} && {settings.HermesCommand} chat{resume} -q '{sq}' -Q --source wpf");

        MaybeLogDiagnosticScript(settings, script, "chat");
        var raw = await ExecuteWslArgvAsync(BuildWslArgv(settings, script), timeoutSeconds).ConfigureAwait(false);
        var (sessionId, displayText) = HermesChatResponseParser.Parse(raw.CombinedText);
        return new HermesExecutionResult
        {
            ExitCode = raw.ExitCode,
            CombinedText = raw.CombinedText,
            LastStderrLine = raw.LastStderrLine,
            SessionId = sessionId,
            DisplayText = displayText,
        };
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

    /// <summary>Redacts the <c>-q '…'</c> prompt payload for logs (length only).</summary>
    private static string SanitizeChatScriptForLog(string script)
    {
        foreach (var marker in new[] { "-q '", "-z \"" })
        {
            var idx = script.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                continue;
            }

            if (marker.EndsWith('\''))
            {
                var contentStart = idx + marker.Length;
                var end = script.IndexOf("' ", contentStart, StringComparison.Ordinal);
                if (end < 0)
                {
                    end = script.LastIndexOf('\'');
                }

                if (end > contentStart)
                {
                    var len = end - contentStart;
                    return script[..contentStart] + $"<omitted len={len}>'" + script[(end + 1)..];
                }
            }
            else
            {
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
            }
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

    /// <summary>
    /// mkdir/export in the same bash -lc as <c>hermes chat</c> (avoids a second wsl.exe while WSL is busy).
    /// Runs after <c>cd</c> to the project dir when <paramref name="wslWorkDir"/> is set.
    /// </summary>
    private static string BuildInlineToolTempPrelude(string wslWorkDir)
    {
        var hasProjectDir = !string.IsNullOrWhiteSpace(wslWorkDir);
        var projectMkdir = hasProjectDir ? " \"$(pwd)/hermes/screenshots\"" : string.Empty;
        var screenshotExport = hasProjectDir
            ? "HERMES_SCREENSHOT_DIR=\"$(pwd)/hermes/screenshots\""
            : "HERMES_SCREENSHOT_DIR=\"$HOME/.hermes/tmp/screenshots\"";

        return
            "test -n \"$HOME\" && test \"${HOME#/}\" != \"$HOME\" && "
            + "mkdir -p \"$HOME/.hermes/tmp\" \"$HOME/.hermes/tmp/xdg-cache\" \"$HOME/.hermes/tmp/xdg-config\" "
            + "\"$HOME/.hermes/tmp/xdg-data\" \"$HOME/.hermes/tmp/playwright-browsers\" \"$HOME/.hermes/tmp/screenshots\""
            + projectMkdir
            + " && export TMPDIR=\"$HOME/.hermes/tmp\" TMP=\"$HOME/.hermes/tmp\" TEMP=\"$HOME/.hermes/tmp\" "
            + "XDG_CACHE_HOME=\"$HOME/.hermes/tmp/xdg-cache\" XDG_CONFIG_HOME=\"$HOME/.hermes/tmp/xdg-config\" "
            + "XDG_DATA_HOME=\"$HOME/.hermes/tmp/xdg-data\" "
            + "PLAYWRIGHT_BROWSERS_PATH=\"$HOME/.hermes/tmp/playwright-browsers\" "
            + screenshotExport
            + " && ";
    }

    /// <summary>Caches WSL home after connection preflight (optional; chat no longer depends on this).</summary>
    public async Task WarmUpWslHomeAsync(HermesSettings settings, CancellationToken cancellationToken = default)
    {
        _ = await ResolveWslHomeDirAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveWslHomeDirAsync(HermesSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_wslHomeSync)
        {
            if (!string.IsNullOrWhiteSpace(_cachedWslHomeDir))
            {
                return _cachedWslHomeDir;
            }
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteWslArgvAsync(BuildWslArgv(settings, "cd ~ && pwd"), 45, cancellationToken)
                .ConfigureAwait(false);
            var line = result.CombinedText
                .ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(line) && line.StartsWith("/", StringComparison.Ordinal))
            {
                lock (_wslHomeSync)
                {
                    _cachedWslHomeDir = line;
                }

                _log.LogInfo($"[hermes] WSL home resolved: {_cachedWslHomeDir}");
                return _cachedWslHomeDir;
            }

            if (attempt < 3)
            {
                _log.LogWarn($"[hermes] WSL home resolve attempt {attempt}/3 failed (exit {result.ExitCode})");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
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

    private async Task<HermesExecutionResult> ExecuteWslArgvAsync(
        IReadOnlyList<string> argv,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        await _wslProcessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteWslArgvCoreAsync(argv, timeoutSeconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _wslProcessGate.Release();
        }
    }

    private async Task<HermesExecutionResult> ExecuteWslArgvCoreAsync(
        IReadOnlyList<string> argv,
        int timeoutSeconds,
        CancellationToken cancellationToken)
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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var exitCode = -1;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
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

            lastStderrRaw ??= timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? "Timed out waiting for Hermes (wsl/bash)."
                : "Hermes (wsl/bash) cancelled.";
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
