using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Hermes.Terminals.Shared.Bridge;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class FuturesTerminalBridgeService
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

    public FuturesTerminalBridgeService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public bool IsIntegrationEnabled => _settings().FuturesTerminalIntegrationEnabled;

    public bool IsActiveForSession =>
        IsIntegrationEnabled || _settings().TradingModeEnabled;

    public bool IsTerminalAlive()
    {
        if (!File.Exists(FuturesBridgePaths.HeartbeatFile))
        {
            return false;
        }

        var text = File.ReadAllText(FuturesBridgePaths.HeartbeatFile).Trim();
        return DateTimeOffset.TryParse(text, out var beat)
               && DateTimeOffset.UtcNow - beat < TimeSpan.FromSeconds(12);
    }

    public FuturesTerminalSnapshotSection? TryReadFuturesSection()
    {
        if (!File.Exists(TradingBridgePaths.SnapshotFile))
        {
            return null;
        }

        try
        {
            return UnifiedSnapshotIO.Read(TradingBridgePaths.SnapshotFile).FuturesTerminal;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[futures-bridge] read failed: {ex.Message}");
            return null;
        }
    }

    public string BuildFuturesContextBlockRu()
    {
        var futures = TryReadFuturesSection();
        if (futures is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("### Binance Demo Futures Terminal snapshot");
        sb.AppendLine(
            $"Symbol: {futures.SelectedSymbol} | WS: {futures.WsStatus} | Chart: {futures.ChartInterval} | "
            + $"Price: {futures.LastPrice:N2} ({futures.ChangePercent24h:+#0.00;-#0.00}%) | "
            + $"Credentials: {(futures.HasCredentials ? "ok" : "missing")}");
        sb.AppendLine(
            $"Agent default volume: {futures.DefaultAgentOrderUsdt:F0} USDT | "
            + $"Max margin: {futures.MaxOrderMarginPercent:0.##}% (~{futures.MaxOrderNotionalUsdt:F0} USDT nom. @ {futures.SelectedLeverage}x) | "
            + $"Risk: {(futures.RiskManagementEnabled ? "on" : "off")}");
        sb.AppendLine(
            $"Exposure: {futures.CurrentExposureUsdt:F0}/{futures.MaxTotalExposureUsdt:F0} USDT | "
            + $"Available: {futures.AvailableUsdt:F2} USDT | Wallet: {futures.WalletBalanceUsdt:F2} USDT | "
            + $"Daily PnL: {futures.DailyRealizedPnlUsdt:+#0.00;-#0.00} USDT | "
            + $"Max positions: {futures.MaxOpenPositions} | Max leverage: {futures.MaxLeverage}x");

        sb.AppendLine("**Балансы (USDT-M)**");
        if (futures.Balances.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var b in futures.Balances.Take(8))
            {
                sb.AppendLine($"• {b.Asset}: free={b.Free:N4} locked={b.Locked:N4}");
            }
        }

        sb.AppendLine("**Позиции**");
        if (futures.Positions.Count == 0)
        {
            sb.AppendLine("• (нет открытых)");
        }
        else
        {
            foreach (var p in futures.Positions)
            {
                sb.AppendLine(
                    $"• {p.Symbol} {p.Side} notional={p.NotionalUsdt:F2} USDT size={p.Size} entry={p.EntryPrice:N2} mark={p.MarkPrice:N2} "
                    + $"uPnL={p.UnrealizedPnl:N2} USDT {p.Leverage}x {p.MarginType}");
            }
        }

        sb.AppendLine("**Открытые ордера**");
        if (futures.OpenOrders.Count == 0)
        {
            sb.AppendLine("• (нет)");
        }
        else
        {
            foreach (var o in futures.OpenOrders.Take(12))
            {
                sb.AppendLine(
                    $"• #{o.Id} {o.Symbol} {o.Side} {o.Type} {o.NotionalUsdt:F2} USDT qty={o.Quantity} price={o.Price} stop={o.StopPrice} {o.Status}");
            }
        }

        return sb.ToString().Trim();
    }

    public void TryLaunchTerminal() => EnsureTerminalRunning(force: false);

    public bool EnsureTerminalRunning(bool force = false)
    {
        if (!IsActiveForSession)
        {
            return false;
        }

        if (!force && !_settings().FuturesTerminalAutoLaunch)
        {
            return IsTerminalAlive();
        }

        if (IsTerminalAlive())
        {
            return true;
        }

        return TryLaunchTerminalProcess();
    }

    public async Task<bool> EnsureTerminalReadyAsync(
        bool force = false,
        int timeoutSeconds = 10,
        CancellationToken ct = default)
    {
        if (!IsActiveForSession)
        {
            return false;
        }

        _ = EnsureTerminalRunning(force);

        if (IsTerminalAlive() && TryReadFuturesSection() is not null)
        {
            return true;
        }

        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 2, 30));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(400, ct).ConfigureAwait(false);

            if (IsTerminalAlive())
            {
                _log.LogInfo("[futures-bridge] ready (heartbeat)");
                return true;
            }
        }

        _log.LogWarn($"[futures-bridge] not ready within {timeoutSeconds}s");
        return IsTerminalAlive();
    }

    private bool TryLaunchTerminalProcess()
    {
        var exe = ResolveTerminalExePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            _log.LogWarn($"[futures-bridge] Hermes.BinanceDemoFuturesTerminal.exe not found: {exe ?? "(null)"}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            });
            _log.LogInfo($"[futures-bridge] launched {exe}");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[futures-bridge] launch failed: {ex.Message}");
            return false;
        }
    }

    private string? ResolveTerminalExePath()
    {
        var custom = _settings().FuturesTerminalExePath?.Trim();
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            return custom;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "Hermes.BinanceDemoFuturesTerminal.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        foreach (var cfg in new[] { "Debug", "Release" })
        {
            var dev = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Hermes.BinanceDemoFuturesTerminal",
                "bin", cfg, "net8.0-windows", "Hermes.BinanceDemoFuturesTerminal.exe"));
            if (File.Exists(dev))
            {
                return dev;
            }
        }

        return null;
    }

    public Task<(bool ok, Guid id, string error)> EnqueueCommandAsync(
        FuturesPlatformCommand command,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.Action))
        {
            return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, "action required"));
        }

        try
        {
            FuturesBridgePaths.EnsureRoot();
            lock (CommandFileLock)
            {
                var file = ReadCommandFile();
                command.RequestedBy ??= "Hermes.Wpf";
                file.Pending.Add(command);
                File.WriteAllText(FuturesBridgePaths.CommandsFile, JsonSerializer.Serialize(file, BridgeJson));
            }

            _log.LogInfo($"[futures-bridge] enqueued {command.Action} id={command.Id} symbol={command.Symbol}");
            return Task.FromResult<(bool, Guid, string)>((true, command.Id, ""));
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[futures-bridge] enqueue failed: {ex.Message}");
            return Task.FromResult<(bool, Guid, string)>((false, Guid.Empty, ex.Message));
        }
    }

    public async Task<(bool ok, string body)> WaitResultAsync(
        Guid id,
        int timeoutSeconds = DefaultResultWaitSeconds,
        CancellationToken ct = default)
    {
        var path = Path.Combine(FuturesBridgePaths.BridgeRoot, $"result-{id}.json");
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 5, 60));

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                return (true, json);
            }

            await Task.Delay(300, ct).ConfigureAwait(false);
        }

        var msg =
            $"BinanceDemoFuturesTerminal не ответил за {timeoutSeconds} с. "
            + "Запустите Hermes.BinanceDemoFuturesTerminal.exe — он обрабатывает очередь команд.";
        _log.LogWarn($"[futures-bridge] {msg}");
        return (false, msg);
    }

    private static FuturesPlatformCommandFile ReadCommandFile()
    {
        if (!File.Exists(FuturesBridgePaths.CommandsFile))
        {
            return new FuturesPlatformCommandFile();
        }

        try
        {
            return JsonSerializer.Deserialize<FuturesPlatformCommandFile>(
                       File.ReadAllText(FuturesBridgePaths.CommandsFile), BridgeJson)
                   ?? new FuturesPlatformCommandFile();
        }
        catch
        {
            return new FuturesPlatformCommandFile();
        }
    }
}
