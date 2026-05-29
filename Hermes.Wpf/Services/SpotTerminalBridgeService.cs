using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.Terminals.Shared.Bridge;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class SpotTerminalBridgeService
{
    private const int DefaultResultWaitSeconds = 20;
    private static readonly object CommandFileLock = new();
    private static readonly JsonSerializerOptions BridgeJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public SpotTerminalBridgeService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsIntegrationEnabled => _settings().SpotTerminalIntegrationEnabled;

    /// <summary>Spot terminal is used when integration is on or Hermes is in trading mode.</summary>
    public bool IsActiveForSession =>
        IsIntegrationEnabled || _settings().TradingModeEnabled;

    public bool IsTerminalAlive()
    {
        if (!File.Exists(UnifiedBridgePaths.UnifiedHeartbeatFile))
        {
            return false;
        }

        var text = File.ReadAllText(UnifiedBridgePaths.UnifiedHeartbeatFile).Trim();
        return DateTimeOffset.TryParse(text, out var beat)
               && DateTimeOffset.UtcNow - beat < TimeSpan.FromSeconds(12);
    }

    public SpotTerminalSnapshotSection? TryReadSpotSection()
    {
        if (!File.Exists(TradingBridgePaths.SnapshotFile))
        {
            return null;
        }

        try
        {
            return UnifiedSnapshotIO.Read(TradingBridgePaths.SnapshotFile).SpotTerminal;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[spot-bridge] read failed: {ex.Message}");
            return null;
        }
    }

    public AgentSnapshotSection? TryReadAgentSection()
    {
        if (!File.Exists(TradingBridgePaths.SnapshotFile))
        {
            return null;
        }

        try
        {
            return UnifiedSnapshotIO.Read(TradingBridgePaths.SnapshotFile).Agent;
        }
        catch
        {
            return null;
        }
    }

    public SkillsSnapshotSection? TryReadSkillsSection()
    {
        if (!File.Exists(TradingBridgePaths.SnapshotFile))
        {
            return null;
        }

        try
        {
            return UnifiedSnapshotIO.Read(TradingBridgePaths.SnapshotFile).Skills;
        }
        catch
        {
            return null;
        }
    }

    public string BuildSpotContextBlockRu()
    {
        var unified = File.Exists(TradingBridgePaths.SnapshotFile)
            ? UnifiedSnapshotIO.Read(TradingBridgePaths.SnapshotFile)
            : null;
        if (unified?.SpotTerminal is null && unified?.Agent is null && unified?.Skills is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("### Spot Terminal snapshot");

        if (unified.SpotTerminal is { } spot)
        {
            sb.AppendLine($"Mode: {spot.ExecutionMode} | Feed: {spot.FeedStatus}");
            foreach (var b in spot.Balances.Take(6))
            {
                sb.AppendLine($"  Balance {b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
            }

            foreach (var o in spot.OpenOrders)
            {
                sb.AppendLine($"  Order {o.Id} {o.Symbol} {o.Side} {o.Status} qty={o.Quantity}");
            }
        }

        if (unified.Agent is { } agent)
        {
            sb.AppendLine($"Agent: {agent.SessionState} | thought: {agent.CurrentThought}");
            foreach (var ev in agent.RecentEvents.Take(5))
            {
                sb.AppendLine($"  [{ev.Kind}] {ev.Summary}");
            }
        }

        if (unified.Skills is { } skills)
        {
            sb.AppendLine($"Skills: draft={skills.DraftCount} approved={skills.ApprovedCount}");
            foreach (var sk in skills.Skills.Where(s => s.Status == "Approved").Take(5))
            {
                sb.AppendLine($"  - {sk.Id} {sk.Name}");
            }
        }

        return sb.ToString().Trim();
    }

    public void TryLaunchTerminal() => EnsureTerminalRunning(force: false);

    public bool EnsureTerminalRunning(bool force = false)
    {
        if (!IsActiveForSession)
        {
            _log.LogInfo("[spot-bridge] launch skipped — integration disabled and not in trading mode");
            return false;
        }

        if (!force && !_settings().SpotTerminalAutoLaunch)
        {
            return IsTerminalAlive();
        }

        if (IsTerminalAlive())
        {
            _log.LogInfo("[spot-bridge] terminal already alive (heartbeat ok)");
            return true;
        }

        return TryLaunchTerminalProcess();
    }

    /// <summary>Launch if needed, then poll heartbeat (and snapshot) until ready or timeout.</summary>
    public async Task<bool> EnsureTerminalReadyAsync(
        bool force = false,
        int timeoutSeconds = 8,
        CancellationToken ct = default)
    {
        if (!IsActiveForSession)
        {
            return false;
        }

        _ = EnsureTerminalRunning(force);

        if (IsTerminalAlive() && TryReadSpotSection() is not null)
        {
            return true;
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 2, 30));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(400, ct).ConfigureAwait(false);

            if (!IsTerminalAlive())
            {
                continue;
            }

            var spot = TryReadSpotSection();
            if (spot is not null && (spot.Tickers.Count > 0 || spot.Balances.Count > 0))
            {
                _log.LogInfo("[spot-bridge] ready (heartbeat + snapshot)");
                return true;
            }

            _log.LogInfo("[spot-bridge] ready (heartbeat)");
            return true;
        }

        _log.LogWarn($"[spot-bridge] not ready within {timeoutSeconds}s");
        return IsTerminalAlive();
    }

    private bool TryLaunchTerminalProcess()
    {
        var exe = ResolveTerminalExePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            _log.LogWarn($"[spot-bridge] Hermes.SpotTerminal.exe not found: {exe ?? "(null)"}");
            return false;
        }

        try
        {
            var workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            var deps = Path.Combine(workDir, "Hermes.SpotTerminal.dll");
            if (!File.Exists(deps))
            {
                _log.LogWarn(
                    $"[spot-bridge] incomplete deploy: missing {deps}. Rebuild Hermes.Wpf (copies full SpotTerminal output).");
                return false;
            }

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = workDir,
            });
            _log.LogInfo($"[spot-bridge] launched Hermes.SpotTerminal.exe path={exe}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[spot-bridge] launch failed: {ex.Message}");
            return false;
        }
    }

    private string? ResolveTerminalExePath()
    {
        var custom = _settings().SpotTerminalExePath?.Trim();
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            return custom;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "Hermes.SpotTerminal.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        foreach (var cfg in new[] { "Debug", "Release" })
        {
            var dev = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Hermes.SpotTerminal", "Hermes.SpotTerminal.Wpf",
                "bin", cfg, "net8.0-windows", "Hermes.SpotTerminal.exe"));
            if (File.Exists(dev))
            {
                return dev;
            }
        }

        return null;
    }

    public Task<(bool ok, Guid id, string error)> EnqueueAsync(string jsonCommand, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var command = JsonSerializer.Deserialize<SpotPlatformCommand>(jsonCommand, BridgeJson);
            if (command is null || string.IsNullOrWhiteSpace(command.Action))
            {
                return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, "Некорректная команда (нет action)."));
            }

            return EnqueueCommandAsync(command, ct);
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, ex.Message));
        }
    }

    public Task<(bool ok, Guid id, string error)> EnqueueCommandAsync(SpotPlatformCommand command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.Action))
        {
            return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, "action required"));
        }

        try
        {
            SpotBridgePaths.EnsureRoot();
            lock (CommandFileLock)
            {
                var file = ReadCommandFile();
                command.RequestedBy ??= "Hermes.Wpf";
                file.Pending.Add(command);
                File.WriteAllText(SpotBridgePaths.CommandsFile, JsonSerializer.Serialize(file, BridgeJson));
            }

            _log.LogInfo($"[spot-bridge] enqueued {command.Action} id={command.Id} symbol={command.Symbol}");
            return Task.FromResult<(bool, Guid, string)>((true, command.Id, ""));
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[spot-bridge] enqueue failed: {ex.Message}");
            return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, ex.Message));
        }
    }

    public async Task<(bool ok, string body)> WaitResultAsync(Guid id, int timeoutSeconds = DefaultResultWaitSeconds, CancellationToken ct = default)
    {
        var path = Path.Combine(SpotBridgePaths.BridgeRoot, $"result-{id}.json");
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 5, 60));

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                _log.LogInfo($"[spot-bridge] result id={id} len={json.Length}");
                return (true, json);
            }

            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        var msg =
            $"SpotTerminal не ответил за {timeoutSeconds} с (нет result-{id}.json). "
            + "Откройте окно Hermes.SpotTerminal — оно обрабатывает очередь команд.";
        _log.LogWarn($"[spot-bridge] {msg}");
        return (false, msg);
    }

    private static SpotPlatformCommandFile ReadCommandFile()
    {
        if (!File.Exists(SpotBridgePaths.CommandsFile))
        {
            return new SpotPlatformCommandFile();
        }

        try
        {
            return JsonSerializer.Deserialize<SpotPlatformCommandFile>(
                       File.ReadAllText(SpotBridgePaths.CommandsFile), BridgeJson)
                   ?? new SpotPlatformCommandFile();
        }
        catch
        {
            return new SpotPlatformCommandFile();
        }
    }

    private string? ResolveCliPath()
    {
        var custom = _settings().SpotTerminalCliPath?.Trim();
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            return custom;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "Hermes.SpotTerminal.Cli.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var dev = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Hermes.SpotTerminal", "Hermes.SpotTerminal.Cli",
            "bin", "Release", "net8.0", "Hermes.SpotTerminal.Cli.exe"));
        return File.Exists(dev) ? dev : null;
    }
}
