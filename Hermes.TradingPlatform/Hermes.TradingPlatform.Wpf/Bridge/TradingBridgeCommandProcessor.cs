using System.IO;
using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Shared.Bridge;
using Hermes.TradingPlatform.Wpf.Services;
using Hermes.TradingPlatform.Wpf.Threading;

namespace Hermes.TradingPlatform.Wpf.Bridge;

public sealed class TradingBridgeCommandProcessor : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly TradingPlatformHost _host;
    private readonly Timer _timer;
    private readonly object _sync = new();

    public TradingBridgeCommandProcessor(TradingPlatformHost host)
    {
        _host = host;
        TradingBridgePaths.EnsureRoot();
        if (!File.Exists(TradingBridgePaths.CommandsFile))
        {
            File.WriteAllText(TradingBridgePaths.CommandsFile, JsonSerializer.Serialize(new TradingPlatformCommandFile(), JsonOptions));
        }

        _timer = new Timer(_ => ProcessPending(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void ProcessPending()
    {
        List<TradingPlatformCommand> commands;
        lock (_sync)
        {
            if (!File.Exists(TradingBridgePaths.CommandsFile))
            {
                return;
            }

            TradingPlatformCommandFile? file;
            try
            {
                file = JsonSerializer.Deserialize<TradingPlatformCommandFile>(File.ReadAllText(TradingBridgePaths.CommandsFile), JsonOptions);
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
            File.WriteAllText(TradingBridgePaths.CommandsFile, JsonSerializer.Serialize(new TradingPlatformCommandFile(), JsonOptions));
        }

        foreach (var cmd in commands)
        {
            WpfThreading.RunOnUi(() => Execute(cmd));
        }
    }

    private void Execute(TradingPlatformCommand cmd)
    {
        TradingPlatformFileLogger.Instance.Bridge($"execute id={cmd.Id} action={cmd.Action} symbol={cmd.Symbol} side={cmd.Side} qty={cmd.Quantity} ro={cmd.ReduceOnly}");
        var result = cmd.Action.ToLowerInvariant() switch
        {
            "place_order" => ExecutePlaceOrder(cmd),
            "close_position" => ExecuteClosePosition(cmd),
            "cancel_order" => ExecuteCancel(cmd),
            "enable_strategy" or "set_strategy" => ExecuteStrategy(cmd),
            "emergency_stop" => ExecuteEmergencyStop(cmd),
            _ => new TradingPlatformCommandResultFile
            {
                CommandId = cmd.Id,
                Success = false,
                Message = $"Unknown action: {cmd.Action}",
            },
        };

        TradingPlatformFileLogger.Instance.Bridge($"result id={cmd.Id} ok={result.Success} msg={result.Message}");
        WriteResult(result);
    }

    private TradingPlatformCommandResultFile ExecutePlaceOrder(TradingPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.Symbol) || string.IsNullOrWhiteSpace(cmd.Side) || cmd.Quantity is null or <= 0)
        {
            return Fail(cmd, "place_order requires symbol, side, quantity");
        }

        if (!Enum.TryParse<OrderSide>(cmd.Side, true, out var side))
        {
            return Fail(cmd, $"Invalid side: {cmd.Side}");
        }

        var type = OrderType.Market;
        if (!string.IsNullOrWhiteSpace(cmd.OrderType) && !Enum.TryParse<OrderType>(cmd.OrderType, true, out type))
        {
            return Fail(cmd, $"Invalid order type: {cmd.OrderType}");
        }

        var price = cmd.Price ?? 0m;
        if (type != OrderType.Market && price <= 0)
        {
            return Fail(cmd, "limit/stop requires price");
        }

        if (type == OrderType.Market)
        {
            var ticker = _host.StateStore.Snapshot.Tickers.FirstOrDefault(t => t.Symbol == cmd.Symbol);
            price = ticker?.Price ?? 0m;
        }

        var reduceOnly = cmd.ReduceOnly ?? false;
        var order = _host.Exchange.PlaceOrder(
            cmd.Symbol!,
            type,
            side,
            cmd.Quantity.Value,
            price,
            reduceOnly);

        TradingPlatformFileLogger.Instance.Exchange(
            $"place_order {order.Id} {order.Status} {order.Symbol} {order.Side} qty={order.Quantity} RO={reduceOnly}");

        return new TradingPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = order.Status != OrderStatus.Rejected,
            Message = $"Order {order.Id} {order.Status} ({order.Symbol} {order.Side} {order.Type} {order.Quantity} RO={reduceOnly})",
        };
    }

    private TradingPlatformCommandResultFile ExecuteClosePosition(TradingPlatformCommand cmd)
    {
        var known = _host.StateStore.Snapshot.Tickers.Select(t => t.Symbol).ToList();
        var symbol = TradingSymbolResolver.Resolve(cmd.Symbol, known);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return Fail(cmd, "close_position requires symbol");
        }

        TradingPlatformFileLogger.Instance.Exchange($"close_position bridge {symbol}");
        var order = _host.Exchange.ClosePosition(symbol, cmd.Quantity);
        var ok = order.Status != OrderStatus.Rejected;
        return new TradingPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = ok,
            Message = ok
                ? $"Closed {symbol} via {order.Id} ({order.Side} {order.Quantity})"
                : $"Close failed: {order.Status}",
        };
    }

    private TradingPlatformCommandResultFile ExecuteCancel(TradingPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.OrderId))
        {
            return Fail(cmd, "cancel_order requires order_id");
        }

        var ok = _host.Exchange.TryCancelOrder(cmd.OrderId);
        return new TradingPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = ok,
            Message = ok ? $"Cancelled {cmd.OrderId}" : $"Could not cancel {cmd.OrderId}",
        };
    }

    private TradingPlatformCommandResultFile ExecuteStrategy(TradingPlatformCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.StrategyId) || cmd.Enabled is null)
        {
            return Fail(cmd, "enable_strategy requires strategy_id and enabled");
        }

        _host.SetStrategyEnabled(cmd.StrategyId, cmd.Enabled.Value);
        return new TradingPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = true,
            Message = $"Strategy {cmd.StrategyId} enabled={cmd.Enabled}",
        };
    }

    private TradingPlatformCommandResultFile ExecuteEmergencyStop(TradingPlatformCommand cmd)
    {
        _host.EmergencyStop(cmd.RequestedBy ?? "Bridge command from Hermes.Wpf");
        return new TradingPlatformCommandResultFile
        {
            CommandId = cmd.Id,
            Success = true,
            Message = "Emergency halt activated",
        };
    }

    private static TradingPlatformCommandResultFile Fail(TradingPlatformCommand cmd, string message) =>
        new() { CommandId = cmd.Id, Success = false, Message = message };

    private static void WriteResult(TradingPlatformCommandResultFile result)
    {
        TradingBridgePaths.EnsureRoot();
        var path = Path.Combine(TradingBridgePaths.RootDirectory, $"result-{result.CommandId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    public void Dispose() => _timer.Dispose();
}
