using System.IO;
using System.Text;
using System.Text.Json;

namespace Hermes.Wpf.Services;

/// <summary>
/// Writes trade commands for HermesWpfTerminal Agent IPC and waits for result.json.
/// </summary>
public sealed class Mt5TerminalIpcClient
{
    private readonly LogService _log;

    public Mt5TerminalIpcClient(LogService log)
    {
        _log = log;
    }

    public static string ResolveIpcDir(string? projectWindowsPath)
    {
        var env = Environment.GetEnvironmentVariable("HERMES_MT5_IPC_DIR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var project = (projectWindowsPath ?? string.Empty).Trim();
        if (project.Length > 0)
        {
            return Path.Combine(project, "hermes", "ipc");
        }

        return @"D:\Programming\AI_Agents\HermesProjects\Mt5Terminal\hermes\ipc";
    }

    public async Task<Mt5TerminalIpcExecutionResult> ExecuteAsync(
        Mt5TerminalRouteCommand command,
        string? projectWindowsPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var dir = ResolveIpcDir(projectWindowsPath);
        Directory.CreateDirectory(dir);

        var commandPath = Path.Combine(dir, "command.json");
        var resultPath = Path.Combine(dir, "result.json");
        var statusPath = Path.Combine(dir, "status.json");

        // Drop stale result so we don't pick up a previous id.
        try
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[mt5-ipc] could not clear result.json: {ex.Message}");
        }

        var json = command.ToCommandJson();
        var tmp = commandPath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        if (File.Exists(commandPath))
        {
            File.Delete(commandPath);
        }

        File.Move(tmp, commandPath);
        _log.LogInfo($"[mt5-ipc] wrote command id={command.Id} action={command.Action} → {commandPath}");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(resultPath))
            {
                string raw;
                try
                {
                    raw = await File.ReadAllTextAsync(resultPath, Encoding.UTF8, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (TryReadResult(raw, out var parsed)
                    && string.Equals(parsed.Id, command.Id, StringComparison.Ordinal))
                {
                    parsed.StatusJson = SafeRead(statusPath);
                    return parsed;
                }
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return new Mt5TerminalIpcExecutionResult
        {
            Ok = false,
            Id = command.Id,
            Action = command.Action,
            Error = $"timeout waiting for HermesWpfTerminal result.json ({timeout.TotalSeconds:0}s). Is the terminal (v33+) open?",
            StatusJson = SafeRead(statusPath)
        };
    }

    public static string FormatChatMessage(Mt5TerminalRouteCommand cmd, Mt5TerminalIpcExecutionResult exec)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Задача: {cmd.Action} (id={cmd.Id})");
        if (exec.Ok)
        {
            sb.AppendLine("Исполнение: OK (HermesWpfTerminal IPC)");
        }
        else
        {
            sb.AppendLine("Исполнение: FAIL");
            if (!string.IsNullOrWhiteSpace(exec.Error))
            {
                sb.AppendLine(exec.Error.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(exec.Message))
        {
            sb.AppendLine(exec.Message.Trim());
        }

        AppendSnapshotFacts(sb, exec.SnapshotJson ?? exec.StatusJson);
        return sb.ToString().TrimEnd();
    }

    private static void AppendSnapshotFacts(StringBuilder sb, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("snapshot", out var snap) && snap.ValueKind == JsonValueKind.Object)
            {
                root = snap;
            }

            if (root.TryGetProperty("real_trading", out var rt))
            {
                sb.AppendLine("real_trading=" + (rt.ValueKind == JsonValueKind.True ? "true" : "false"));
            }

            if (root.TryGetProperty("positions_header", out var ph) && ph.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine("positions: " + ph.GetString());
            }

            if (root.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in positions.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String)
                    {
                        sb.AppendLine("  - " + p.GetString());
                    }
                }
            }

            if (root.TryGetProperty("log_tail", out var logs) && logs.ValueKind == JsonValueKind.Array)
            {
                var lines = logs.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty)
                    .Where(x => x.Length > 0)
                    .TakeLast(8)
                    .ToList();
                if (lines.Count > 0)
                {
                    sb.AppendLine("log_tail:");
                    foreach (var line in lines)
                    {
                        sb.AppendLine("  " + line);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore snapshot parse errors
        }
    }

    private static bool TryReadResult(string raw, out Mt5TerminalIpcExecutionResult result)
    {
        result = new Mt5TerminalIpcExecutionResult();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            result.Ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            result.Id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
            result.Action = root.TryGetProperty("action", out var aEl) && aEl.ValueKind == JsonValueKind.String
                ? aEl.GetString() ?? string.Empty
                : string.Empty;
            result.Message = root.TryGetProperty("message", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString()
                : null;
            result.Error = root.TryGetProperty("error", out var eEl) && eEl.ValueKind == JsonValueKind.String
                ? eEl.GetString()
                : null;
            if (root.TryGetProperty("snapshot", out var snap))
            {
                result.SnapshotJson = snap.GetRawText();
            }

            return !string.IsNullOrWhiteSpace(result.Id);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class Mt5TerminalIpcExecutionResult
{
    public bool Ok { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string? SnapshotJson { get; set; }
    public string? StatusJson { get; set; }
}
