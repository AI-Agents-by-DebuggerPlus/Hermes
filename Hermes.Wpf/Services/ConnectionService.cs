using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// WSL preflight / install with distro auto-resolution, ignoring Docker WSL adapters and falling back safely.
/// </summary>
public sealed class ConnectionService
{
    /// <summary>Ask WSL to use UTF-8 for console/pipe IO so redirected streams decode correctly on Windows.</summary>
    private const string WslUtf8EnvVar = "WSL_UTF8";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly LogService _log;
    private readonly SettingsService _settingsService;

    /// <summary>Append <see cref="WslUtf8EnvVar"/> — UTF-16 decode of bash output corrupts Cyrillic/UI text.</summary>
    private static void ApplyCommonWslUtf8(ProcessStartInfo psi)
    {
        psi.Environment[WslUtf8EnvVar] = "1";
    }

    public ConnectionService(LogService logService, SettingsService settingsService)
    {
        _log = logService;
        _settingsService = settingsService;
    }

    private void Log(string message) => _log.LogInfo("[connection] " + message);

    public async Task<ConnectionStatus> RunPreflightAsync(HermesSettings settings, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<CheckResult>();

        try
        {
            var wslStat = await CheckWslStatusAsync(cancellationToken);
            diagnostics.Add(new CheckResult
            {
                Ok = wslStat.Success,
                Label = "WSL status",
                Detail = TruncateDetail(wslStat.Output),
                FixHint = wslStat.Success ? null : "Install or repair WSL: wsl --install"
            });
            if (!wslStat.Success)
            {
                return FinalStatus(ConnectionState.Error, BuildMessage(false, diagnostics), diagnostics);
            }

            var distros = await GetAvailableDistrosAsync(cancellationToken);
            var distroLabels = distros.Count == 0 ? "(none listed)" : string.Join(", ", distros);
            diagnostics.Add(new CheckResult
            {
                Ok = distros.Any(),
                Label = $"WSL distros: {distroLabels}",
                FixHint = distros.Any() ? null : "Install a Linux distro: wsl --install -d Ubuntu"
            });

            var resolvedPreview = ResolveWorkingDistro(distros, settings);
            diagnostics.Add(new CheckResult
            {
                Ok = distros.Any(d => !IsDockerStyleDistro(d)),
                Label = $"Resolved distro for Hermes: {resolvedPreview ?? "(fallback: default WSL)"}",
                FixHint = resolvedPreview == null && !distros.Any(d => !IsDockerStyleDistro(d))
                    ? "Add a non‑Docker distro (e.g. Ubuntu)." : null
            });

            if (!distros.Any())
            {
                return FinalStatus(ConnectionState.Error,
                    BuildMessage(false, diagnostics), diagnostics);
            }

            var bashChecks = new (string Title, string Bash)[]
            {
                ("Venv folder", $"test -d {BashQuotedVenvRoot(settings)} && echo venv_ok || echo venv_missing"),
                ("Hermes CLI",
                    $"source {BashQuotedVenvRoot(settings)}/bin/activate && command -v {settings.HermesCommand}"),
                ("Hermes status",
                    $"source {BashQuotedVenvRoot(settings)}/bin/activate && {settings.HermesCommand} status")
            };

            foreach (var pair in bashChecks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Log($"Preflight step: {pair.Title}");
                var (success, merged, usedDistro) = await ExecuteBashAsync(
                    settings, distros, pair.Bash, timeoutSeconds: 45, cancellationToken);

                Log(success
                    ? $"Preflight step OK [{pair.Title}] distro={usedDistro ?? "(default)"} len={merged.Length}"
                    : $"Preflight step FAIL [{pair.Title}] distro={usedDistro ?? "(default)"} output={TruncateDetail(merged, 240)}");

                var stepOk = success &&
                             !IsLikelyMissingDistroError(merged) &&
                             (pair.Title != "Venv folder" || merged.Contains("venv_ok", StringComparison.Ordinal));

                diagnostics.Add(new CheckResult
                {
                    Ok = stepOk,
                    Label = pair.Title,
                    Detail = TruncateDetail(merged),
                    FixHint = stepOk ? null :
                        pair.Title == "Venv folder"
                            ? "Create or fix venv path, e.g. python3 -m venv ..."
                            : "Confirm Hermes is installed in that venv and PATH."
                });

                if (!stepOk)
                {
                    return FinalStatus(ConnectionState.Error, BuildMessage(false, diagnostics), diagnostics);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            var allGreen = diagnostics.All(d => d.Ok);
            var state = allGreen ? ConnectionState.Connected : ConnectionState.Error;

            Log(state == ConnectionState.Connected ? "Preflight completed successfully." : "Preflight reported errors.");

            return FinalStatus(
                state,
                state == ConnectionState.Connected
                    ? "Preflight succeeded. Hermes is ready."
                    : BuildMessage(false, diagnostics),
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            Log("Preflight canceled.");
            throw;
        }
        catch (Exception ex)
        {
            Log("Preflight exception: " + ex.Message);
            diagnostics.Add(new CheckResult
            {
                Ok = false,
                Label = "Exception",
                Detail = ex.Message
            });

            return FinalStatus(ConnectionState.Error, BuildMessage(false, diagnostics), diagnostics);
        }
    }

    public async Task<ConnectionStatus> InstallHermesAsync(HermesSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var distros = await GetAvailableDistrosAsync(cancellationToken);
            if (distros.Count == 0)
            {
                return new ConnectionStatus
                {
                    State = ConnectionState.Error,
                    Message = "No WSL distributions found. Install Ubuntu: wsl --install -d Ubuntu"
                };
            }

            var cmd =
                $"python3 -m pip install --upgrade pip && python3 -m pip install {settings.HermesCommand}";

            var (success, output, used) = await ExecuteBashAsync(
                settings, distros, cmd, timeoutSeconds: 300, cancellationToken);

            Log($"InstallHermes finished success={success} distro={used ?? "(default)"}");

            if (!success)
            {
                return new ConnectionStatus
                {
                    State = ConnectionState.Error,
                    Message = $"Install failed: {TruncateDetail(output, 400)}"
                };
            }

            return new ConnectionStatus
            {
                State = ConnectionState.Connected,
                Message = "Hermes install command completed."
            };
        }
        catch (Exception ex)
        {
            Log("InstallHermes exception: " + ex.Message);
            return new ConnectionStatus { State = ConnectionState.Error, Message = ex.Message };
        }
    }

    /// <summary>
    /// Returns the exact distro name from <c>wsl -l -q</c> that Hermes should use; ignores Docker-style distros.
    /// </summary>
    public async Task<string?> DetectRecommendedDistroAsync(HermesSettings settings, CancellationToken cancellationToken = default)
    {
        var list = await GetAvailableDistrosAsync(cancellationToken);
        return ResolveWorkingDistro(list, settings);
    }

    private static ConnectionStatus FinalStatus(ConnectionState state, string message, List<CheckResult> diagnostics) =>
        new()
        {
            State = state,
            Message = message,
            Diagnostics = diagnostics
        };

    private static string BuildMessage(bool success, IReadOnlyList<CheckResult> rows)
    {
        if (success)
        {
            return "Preflight succeeded. Hermes is ready.";
        }

        var failed = rows.Where(r => !r.Ok).ToList();
        if (failed.Count == 0)
        {
            return "Preflight failed.";
        }

        return string.Join(Environment.NewLine,
            failed.Select(f => string.IsNullOrWhiteSpace(f.Detail) ? f.Label : $"{f.Label}: {f.Detail}"));
    }

    private static string TruncateDetail(string? text, int max = 500)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var t = text.Trim().Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        return t.Length <= max ? t : t[..max] + "…";
    }

    private async Task<IReadOnlyList<string>> GetAvailableDistrosAsync(CancellationToken cancellationToken)
    {
        // Must use the same UTF-8 (+ WSL_UTF8) path as ExecuteBashAsync. UTF-16 / mixed decode here produced
        // garbage distro labels so -d "Ubuntu" failed and we fell through to docker-desktop (no bash).
        var (_, rawQuiet) = await RunWslProcessAsync(["-l", "-q"], 20, cancellationToken);
        var fromQuiet = ParseQuietDistroList(rawQuiet);
        if (fromQuiet.Count > 0)
        {
            Log($"wsl -l -q resolved {fromQuiet.Count} distro(s): {string.Join(", ", fromQuiet)}");
            return fromQuiet;
        }

        Log("wsl -l -q returned nothing usable; parsing wsl -l -v.");
        var (_, rawVerbose) = await RunWslProcessAsync(["-l", "-v"], 25, cancellationToken);
        var fromVerbose = ParseVerboseDistroList(rawVerbose);
        if (fromVerbose.Count > 0)
        {
            Log($"wsl -l -v resolved {fromVerbose.Count} distro(s): {string.Join(", ", fromVerbose)}");
        }
        else
        {
            Log("Could not enumerate WSL distro names (-l -q and -l -v).");
        }

        return fromVerbose;
    }

    private static IReadOnlyList<string> ParseQuietDistroList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var lines = raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => NormalizeDistroLabel(line))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return DistinctPreserveOrder(lines);
    }

    private static IReadOnlyList<string> ParseVerboseDistroList(string raw)
    {
        var lines = raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (lines.Count < 2)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();

        foreach (var line in lines.Skip(1))
        {
            var trimmed = NormalizeDistroLabel(line.TrimStart());
            if (trimmed.StartsWith("*", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..].TrimStart();
            }

            if (trimmed.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = Regex.Split(trimmed, @"\s{2,}");
            if (parts.Length == 0)
            {
                continue;
            }

            var name = NormalizeDistroLabel(parts[0]);
            if (string.IsNullOrWhiteSpace(name) ||
                string.Equals(name, "NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            names.Add(name);
        }

        return DistinctPreserveOrder(names);
    }

    /// <summary>Trim WSL quirks: default-marker *, invisible direction marks, BOM.</summary>
    private static string NormalizeDistroLabel(string s)
    {
        s = s.Trim().TrimEnd('*').Trim();
        return s.Replace("\u200e", "")
            .Replace("\u200f", "")
            .Replace("\ufeff", "", StringComparison.Ordinal)
            .Trim();
    }

    private static List<string> DistinctPreserveOrder(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var item in items)
        {
            if (seen.Add(item))
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static bool IsDockerStyleDistro(string distroName)
    {
        return distroName.Contains("docker-desktop", StringComparison.OrdinalIgnoreCase)
               || distroName.Equals("rancher-desktop", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Host-side WSL errors only (avoid substring false positives from Linux output).</summary>
    private static bool IsLikelyMissingDistroError(string combinedOutput)
    {
        if (combinedOutput.Contains("WSL_E_DISTRO_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (combinedOutput.Contains("WSL/Service/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return combinedOutput.Contains("no distribution", StringComparison.OrdinalIgnoreCase)
               && combinedOutput.Contains("supplied name", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Path for bash -lc: ~/… → "$HOME/rest" so /bin/activate appends correctly.</summary>
    private static string BashQuotedVenvRoot(HermesSettings settings)
    {
        var vp = settings.VenvPath.Trim();
        if (string.IsNullOrEmpty(vp))
        {
            return "\"$HOME\"";
        }

        if (vp.StartsWith("~/", StringComparison.Ordinal))
        {
            var rest = EscapeForInnerDoubleQuotedBash(vp[2..]);
            return "\"$HOME/" + rest + "\"";
        }

        if (vp.StartsWith("/", StringComparison.Ordinal))
        {
            return "'" + vp.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }

        return "\"" + EscapeForInnerDoubleQuotedBash(vp) + "\"";
    }

    private static string EscapeForInnerDoubleQuotedBash(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`")
            .Replace("$", "\\$");

    /// <summary>Pick a distro: valid configured name (non-Docker), else Ubuntu, else first non‑Docker.</summary>
    private static string? ResolveWorkingDistro(IReadOnlyList<string> distros, HermesSettings settings)
    {
        if (distros.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.WslDistro))
        {
            var match = distros.FirstOrDefault(d =>
                string.Equals(d, settings.WslDistro, StringComparison.OrdinalIgnoreCase));

            if (match != null && !IsDockerStyleDistro(match))
            {
                return match;
            }
        }

        var ubuntu = distros.FirstOrDefault(d =>
            !IsDockerStyleDistro(d) && d.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase));

        return ubuntu ??
               distros.FirstOrDefault(d => !IsDockerStyleDistro(d) && !d.Contains("docker", StringComparison.OrdinalIgnoreCase))
               ?? distros.FirstOrDefault(d => !IsDockerStyleDistro(d));
    }

    private static List<string> BuildDistroCandidateOrder(IReadOnlyList<string> all, HermesSettings settings)
    {
        var usable = all.Where(d => !IsDockerStyleDistro(d)).ToList();

        var ordered = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.WslDistro))
        {
            var exact = usable.FirstOrDefault(d =>
                string.Equals(d, settings.WslDistro, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                ordered.Add(exact);
            }
        }

        var ubuntu = usable.FirstOrDefault(d => d.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase));
        TryAddDistinct(ubuntu, ordered);

        foreach (var d in usable.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            TryAddDistinct(d, ordered);
        }

        return ordered;
    }

    private static void TryAddDistinct(string? distroName, ICollection<string> list)
    {
        if (distroName is null)
        {
            return;
        }

        if (list.Any(existing => existing.Equals(distroName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        list.Add(distroName);
    }

    private async Task<(bool Success, string Output, string? DistroUsed)> ExecuteBashAsync(
        HermesSettings settings,
        IReadOnlyList<string> allDistros,
        string bashCommand,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        foreach (var distro in BuildDistroCandidateOrder(allDistros, settings))
        {
            var argv = new[]
            {
                "-d",
                distro,
                "--",
                "/bin/bash",
                "-lc",
                bashCommand
            };

            var (exit, combined) = await RunWslProcessAsync(argv, timeoutSeconds, cancellationToken);

            Log($"wsl -d [{distro}] exit={exit} len={combined.Length}");

            // Success first: avoids mis-parsing distro errors when quoting was wrong yet bash ran.
            if (exit == 0)
            {
                await PersistDistroIfChangedAsync(settings, distro);
                return (true, combined.Trim(), distro);
            }

            if (IsLikelyMissingDistroError(combined))
            {
                Log($"WSL distro not available for Hermes ({distro}). Trying next...");
                continue;
            }

            return (false, combined.Trim(), distro);
        }

        var hasNonDockerListed = allDistros.Any(static d => !IsDockerStyleDistro(d));

        // Default WSL (*) is often docker-desktop → no /bin/bash; never fall back blindly.
        if (hasNonDockerListed)
        {
            var hint =
                "Linux distros appear in `wsl -l` but every `wsl -d <name>` failed host-side checks. " +
                "Compare names exactly with `wsl -l -v`; app will not use unnamed default WSL (often docker-desktop without bash).";

            Log(hint);

            return (false, hint, null);
        }

        Log("Only Docker-style adapters listed; optional fallback to default profile (no -d).");

        var (fbExit, fbOut) =
            await RunWslProcessAsync(["--", "/bin/bash", "-lc", bashCommand], timeoutSeconds, cancellationToken);

        if (IsLikelyMissingDistroError(fbOut))
        {
            return (false, fbOut.Trim(), null);
        }

        return (fbExit == 0, fbOut.Trim(), null);
    }

    private async Task PersistDistroIfChangedAsync(HermesSettings settings, string distroNameFromList)
    {
        if (string.Equals(settings.WslDistro, distroNameFromList, StringComparison.Ordinal))
        {
            return;
        }

        Log($"Updating settings WslDistro: '{settings.WslDistro}' -> '{distroNameFromList}'.");
        settings.WslDistro = distroNameFromList;
        await _settingsService.SaveAsync(settings);
    }

    private async Task<(int ExitCode, string Combined)> RunWslProcessAsync(
        IReadOnlyList<string> wslArgv,
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

        ApplyCommonWslUtf8(psi);

        psi.ArgumentList.Clear();
        foreach (var chunk in wslArgv)
        {
            psi.ArgumentList.Add(chunk);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                sb.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                sb.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            Log("wsl invocation timed out.");
            return (-1, "Timed out.");
        }

        var text = NormalizeWslCapturedText(sb.ToString());
        return (process.ExitCode, text);
    }

    /// <summary>
    /// If infra text was decoded with wrong endianness/size, NULs leak between BMP chars — strip then trim.
    /// </summary>
    private static string NormalizeWslCapturedText(string combined)
    {
        if (!combined.Contains('\0', StringComparison.Ordinal))
        {
            return combined.Trim();
        }

        return combined.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
    }

    private async Task<(bool Success, string Output)> CheckWslStatusAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) =
            await RunWslProcessAsync(["--status"], 15, cancellationToken).ConfigureAwait(false);

        output = NormalizeWslCapturedText(output);

        if (exitCode != 0)
        {
            return (false,
                string.IsNullOrWhiteSpace(output) ? $"Exit code {exitCode}" : output);
        }

        return (true, output);
    }
}
