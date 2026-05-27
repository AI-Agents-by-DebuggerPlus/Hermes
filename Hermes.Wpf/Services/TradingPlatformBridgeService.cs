using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.Wpf.Models;

// AUDIT 2026-05-25 (TradingExperienceExporter integration point):
//   TryReadSnapshot() is the single hot path where Hermes.Wpf observes the trading platform.
//   It is invoked from MainViewModel.ExecuteHermesUserTurnAsync (prompt build) and from a polling timer in MainWindow startup.
//   SnapshotUpdated event fires AFTER a successful deserialise → this is the hook to feed TradingExperienceExporter.OnSnapshotUpdatedAsync.
//   Snapshot DTO matches Docs/Report/Hermes_Trading_Platform_Integration.md (Account/Pnl/Risk/Positions/Orders/Strategies + Tickers + RecentLogs).
//   Δ vs doc: RecentLogs / Tickers / Hermes orchestrator block are present in code but only briefly described in the doc; structure is otherwise stable.

namespace Hermes.Wpf.Services;

public sealed class TradingPlatformBridgeService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;

    public event Action<TradingPlatformSnapshotFile>? SnapshotUpdated;

    public TradingPlatformBridgeService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsIntegrationEnabled => _settings().TradingPlatformIntegrationEnabled;

    public bool IsTerminalAlive()
    {
        if (!File.Exists(TradingBridgePaths.HeartbeatFile))
        {
            return false;
        }

        var text = File.ReadAllText(TradingBridgePaths.HeartbeatFile).Trim();
        return DateTimeOffset.TryParse(text, out var beat)
               && DateTimeOffset.UtcNow - beat < TimeSpan.FromSeconds(12);
    }

    public TradingPlatformSnapshotFile? TryReadSnapshot()
    {
        if (!File.Exists(TradingBridgePaths.SnapshotFile))
        {
            return null;
        }

        try
        {
            var snap = JsonSerializer.Deserialize<TradingPlatformSnapshotFile>(
                File.ReadAllText(TradingBridgePaths.SnapshotFile),
                JsonOptions);
            if (snap is not null)
            {
                SnapshotUpdated?.Invoke(snap);
            }

            return snap;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[trading-bridge] snapshot read failed: {ex.Message}");
            return null;
        }
    }

    public string BuildSnapshotContextBlockRu(TradingPlatformSnapshotFile snap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Trading Platform snapshot (live)");
        sb.AppendLine($"Время: {snap.TimestampUtc:u} | Feed: {snap.FeedStatus} | {snap.MarketDataSource}");
        sb.AppendLine($"Balance {snap.Account.Balance:N2} | Equity {snap.Account.Equity:N2} | Free {snap.Account.FreeMargin:N2} | Lev {snap.Account.Leverage:F1}x");
        sb.AppendLine($"PnL today {snap.Pnl.Today:N2} | Risk {snap.Risk.RiskLevel} DD {snap.Risk.DailyDrawdownPercent:F1}% exp {snap.Risk.ExposurePercent:F1}% SafeMode={snap.Risk.SafeMode} Halt={snap.Risk.EmergencyHalt}");
        sb.AppendLine($"Orchestrator: {snap.Hermes.State} | {snap.Hermes.ActiveStrategy} | conf {snap.Hermes.Confidence:P0}");
        sb.AppendLine($"Reasoning: {snap.Hermes.CurrentReasoning}");
        if (snap.Positions.Count > 0)
        {
            sb.AppendLine("Positions:");
            foreach (var p in snap.Positions)
            {
                sb.AppendLine($"  - {p.Symbol} {p.Side} size={p.Size} entry={p.EntryPrice:N2} mark={p.MarkPrice:N2} uPnL={p.UnrealizedPnl:N2}");
            }
        }
        else
        {
            sb.AppendLine("Positions: (none)");
        }

        var open = snap.Orders.Where(o => o.Status == "Open").ToList();
        if (open.Count > 0)
        {
            sb.AppendLine("Open orders:");
            foreach (var o in open)
            {
                sb.AppendLine($"  - {o.Id} {o.Symbol} {o.Side} {o.Type} qty={o.Quantity} price={o.Price:N2} RO={o.ReduceOnly}");
            }
        }
        else
        {
            sb.AppendLine("Open orders: (none)");
        }

        sb.AppendLine("Strategies: " + string.Join(", ", snap.Strategies.Select(s => $"{s.Id}({(s.IsEnabled ? "on" : "off")})")));
        return sb.ToString().Trim();
    }

    public async Task<(bool Ok, string Detail)> TryEnqueueCommandAsync(TradingPlatformCommand command, CancellationToken ct = default)
    {
        var cli = ResolveCliPath();
        if (cli is null)
        {
            return (false, "Hermes.TradingPlatform.Cli не найден. Соберите solution Hermes.TradingPlatform.");
        }

        var json = JsonSerializer.Serialize(command, JsonOptions);
        _log.LogInfo($"[trading-bridge] enqueue action={command.Action} id={command.Id} json={json}");
        var psi = new ProcessStartInfo(cli, ["enqueue", json])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return (false, "Не удалось запустить CLI");
            }

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                return (false, string.IsNullOrWhiteSpace(stderr) ? $"CLI exit {proc.ExitCode}" : stderr.Trim());
            }

            var commandId = stdout.Trim();
            if (!Guid.TryParse(commandId, out var id))
            {
                return (false, "CLI не вернул command id");
            }

            var waitPsi = new ProcessStartInfo(cli, ["wait-result", id.ToString(), "--timeout=20"])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var waitProc = Process.Start(waitPsi);
            if (waitProc is null)
            {
                return (true, $"Команда {id} поставлена в очередь (результат не получен)");
            }

            var resultOut = await waitProc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var resultErr = await waitProc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await waitProc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (waitProc.ExitCode != 0)
            {
                _log.LogWarn($"[trading-bridge] wait-result failed id={id} err={resultErr.Trim()}");
                return (false, string.IsNullOrWhiteSpace(resultErr) ? "Таймаут или терминал не обработал команду" : resultErr.Trim());
            }

            _log.LogInfo($"[trading-bridge] result id={id} body={resultOut.Trim()}");
            return (true, resultOut.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void TryLaunchTerminal() => EnsureTerminalRunning(force: false);

    /// <summary>Launch terminal when integration is on; <paramref name="force"/> ignores auto-launch setting.</summary>
    public bool EnsureTerminalRunning(bool force = false)
    {
        if (!IsIntegrationEnabled)
        {
            _log.LogInfo("[trading-bridge] launch skipped — integration disabled");
            return false;
        }

        if (!force && !_settings().TradingPlatformAutoLaunchTerminal)
        {
            return IsTerminalAlive();
        }

        if (IsTerminalAlive())
        {
            _log.LogInfo("[trading-bridge] terminal already alive (heartbeat ok)");
            return true;
        }

        var exe = ResolveTerminalExePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            _log.LogWarn($"[trading-bridge] TradingPlatform.exe not found: {exe ?? "(null)"}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            _log.LogInfo($"[trading-bridge] launched Hermes.TradingPlatform.exe path={exe}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[trading-bridge] launch failed: {ex.Message}");
            return false;
        }
    }

    private string? ResolveCliPath()
    {
        var custom = _settings().TradingPlatformCliPath?.Trim();
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            return custom;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Hermes.TradingPlatform.Cli.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var dev = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Hermes.TradingPlatform", "Hermes.TradingPlatform.Cli", "bin", "Release", "net8.0", "Hermes.TradingPlatform.Cli.exe"));
        return File.Exists(dev) ? dev : null;
    }

    private string? ResolveTerminalExePath()
    {
        var custom = _settings().TradingPlatformExePath?.Trim();
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            return custom;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "Hermes.TradingPlatform.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Hermes.TradingPlatform", "Hermes.TradingPlatform.Wpf", "bin", "Release", "net8.0-windows", "Hermes.TradingPlatform.exe"));
        return File.Exists(dev) ? dev : null;
    }
}
