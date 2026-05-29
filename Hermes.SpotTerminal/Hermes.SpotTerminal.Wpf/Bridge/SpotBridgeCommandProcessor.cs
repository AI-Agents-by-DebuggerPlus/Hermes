using System.IO;
using System.Text.Json;
using Hermes.SpotTerminal.Core.Enums;
using SpotOrderSide = Hermes.SpotTerminal.Core.Enums.SpotOrderSide;
using SpotOrderType = Hermes.SpotTerminal.Core.Enums.SpotOrderType;
using SpotOrderStatus = Hermes.SpotTerminal.Core.Enums.SpotOrderStatus;
using ExecutionMode = Hermes.SpotTerminal.Core.Enums.ExecutionMode;
using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.SpotTerminal.Wpf.Services;

namespace Hermes.SpotTerminal.Wpf.Bridge;

public sealed class SpotBridgeCommandProcessor : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private readonly SpotTerminalHost _host;
    private readonly Timer _timer;
    private readonly object _sync = new();

    public SpotBridgeCommandProcessor(SpotTerminalHost host)
    {
        _host = host;
        SpotBridgePaths.EnsureRoot();
        if (!File.Exists(SpotBridgePaths.CommandsFile))
        {
            File.WriteAllText(SpotBridgePaths.CommandsFile, JsonSerializer.Serialize(new SpotPlatformCommandFile(), JsonOptions));
        }

        _timer = new Timer(_ => ProcessPending(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void ProcessPending()
    {
        List<SpotPlatformCommand> commands;
        lock (_sync)
        {
            if (!File.Exists(SpotBridgePaths.CommandsFile))
            {
                return;
            }

            SpotPlatformCommandFile? file;
            try
            {
                file = JsonSerializer.Deserialize<SpotPlatformCommandFile>(File.ReadAllText(SpotBridgePaths.CommandsFile), JsonOptions);
            }
            catch
            {
                return;
            }

            if (file?.Pending is not { Count: > 0 })
            {
                return;
            }

            commands = file.Pending.ToList();
            File.WriteAllText(SpotBridgePaths.CommandsFile, JsonSerializer.Serialize(new SpotPlatformCommandFile(), JsonOptions));
        }

        foreach (var cmd in commands)
        {
            var copy = cmd;
            _ = Task.Run(() => ExecuteSafe(copy));
        }
    }

    private void ExecuteSafe(SpotPlatformCommand cmd)
    {
        SpotTerminalFileLogger.Instance.Bridge(
            $"execute id={cmd.Id} action={cmd.Action} symbol={cmd.Symbol} side={cmd.Side} qty={cmd.Quantity}");
        try
        {
            Execute(cmd);
        }
        catch (Exception ex)
        {
            SpotTerminalFileLogger.Instance.Error($"bridge execute failed id={cmd.Id}: {ex.Message}");
            WriteResult(Fail(cmd, ex.Message));
        }
    }

    private void Execute(SpotPlatformCommand cmd)
    {
        var result = cmd.Action.ToLowerInvariant() switch
        {
            "place_order" => ExecutePlace(cmd),
            "cancel_order" => ExecuteCancel(cmd),
            "set_mode" => ExecuteSetMode(cmd),
            "approve_skill" => ExecuteApproveSkill(cmd),
            "backtest_skill" => ExecuteBacktestSkill(cmd),
            _ => Fail(cmd, $"Unknown action: {cmd.Action}"),
        };

        WriteResult(result);
        SpotTerminalFileLogger.Instance.Bridge($"result id={cmd.Id} ok={result.Success} msg={result.Message}");
    }

    private SpotPlatformCommandResultFile ExecutePlace(SpotPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol) || string.IsNullOrWhiteSpace(cmd.Side) || cmd.Quantity is null or <= 0)
        {
            return Fail(cmd, "place_order requires symbol, side, quantity");
        }

        if (!Enum.TryParse<SpotOrderSide>(cmd.Side, true, out var side))
        {
            return Fail(cmd, "invalid side");
        }

        var type = Enum.TryParse<SpotOrderType>(cmd.OrderType ?? "Market", true, out var t) ? t : SpotOrderType.Market;
        var price = cmd.Price ?? _host.StateStore.Snapshot.Tickers.FirstOrDefault(x => x.Symbol == cmd.Symbol)?.Price ?? 0m;
        var order = _host.Gateway.PlaceOrderAsync(cmd.Symbol!, type, side, cmd.Quantity.Value, price).GetAwaiter().GetResult();
        return new SpotPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = order.Status != SpotOrderStatus.Rejected,
            Message = $"Order {order.Id} {order.Status}",
        };
    }

    private SpotPlatformCommandResultFile ExecuteCancel(SpotPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.OrderId) || string.IsNullOrWhiteSpace(cmd.Symbol))
        {
            return Fail(cmd, "cancel_order requires order_id and symbol");
        }

        var ok = _host.Gateway.CancelOrderAsync(cmd.Symbol, cmd.OrderId).GetAwaiter().GetResult();
        return new SpotPlatformCommandResultFile { CommandId = cmd.Id, Success = ok, Message = ok ? "Cancelled" : "Failed" };
    }

    private SpotPlatformCommandResultFile ExecuteSetMode(SpotPlatformCommand cmd)
    {
        if (!Enum.TryParse<ExecutionMode>(cmd.OrderType ?? "Virtual", true, out var mode))
        {
            mode = ExecutionMode.Virtual;
        }

        _host.SetExecutionMode(mode);
        return new SpotPlatformCommandResultFile { CommandId = cmd.Id, Success = true, Message = $"Mode={mode}" };
    }

    private SpotPlatformCommandResultFile ExecuteApproveSkill(SpotPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.SkillId))
        {
            return Fail(cmd, "skill_id required");
        }

        _host.SkillLifecycle.Approve(cmd.SkillId);
        return new SpotPlatformCommandResultFile { CommandId = cmd.Id, Success = true, Message = "Skill approved" };
    }

    private SpotPlatformCommandResultFile ExecuteBacktestSkill(SpotPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.SkillId))
        {
            return Fail(cmd, "skill_id required");
        }

        var summary = _host.SkillLifecycle.RunBacktest(cmd.SkillId);
        return new SpotPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = summary.PassedThreshold,
            Message = $"Backtest PnL={summary.NetPnl:N2}",
        };
    }

    private static SpotPlatformCommandResultFile Fail(SpotPlatformCommand cmd, string message) =>
        new() { CommandId = cmd.Id, Success = false, Message = message };

    private static void WriteResult(SpotPlatformCommandResultFile result)
    {
        SpotBridgePaths.EnsureRoot();
        var path = Path.Combine(SpotBridgePaths.BridgeRoot, $"result-{result.CommandId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose() => _timer.Dispose();
}
