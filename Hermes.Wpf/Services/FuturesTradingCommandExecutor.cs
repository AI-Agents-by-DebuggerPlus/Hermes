using Hermes.Terminals.Shared.Bridge;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

internal static class FuturesTradingCommandExecutor
{
    public static async Task<(bool Ok, string Detail)> ExecuteAsync(
        FuturesTerminalBridgeService futures,
        TradingPlatformCommand tradingCmd,
        CancellationToken ct = default)
    {
        if (!await futures.EnsureTerminalReadyAsync(force: true, ct: ct).ConfigureAwait(false))
        {
            return (false, "Hermes.BinanceDemoFuturesTerminal.exe не запущен. Нажмите Binance Futures или включите автозапуск.");
        }

        var action = MapAction(tradingCmd.Action);
        if (action is null)
        {
            return (false, $"Действие не поддерживается в Futures Terminal: {tradingCmd.Action}");
        }

        var futuresCmd = new FuturesPlatformCommand
        {
            Action = action,
            Symbol = tradingCmd.Symbol,
            Side = NormalizeSide(tradingCmd.Side),
            OrderType = NormalizeOrderType(tradingCmd.OrderType),
            QuantityUsdt = tradingCmd.QuantityUsdt ?? tradingCmd.Quantity,
            Price = tradingCmd.Price > 0 ? tradingCmd.Price : null,
            ReduceOnly = tradingCmd.ReduceOnly,
            OrderId = tradingCmd.OrderId,
            Leverage = tradingCmd.Leverage,
            RequestedBy = tradingCmd.RequestedBy ?? "Hermes.Wpf",
        };

        var (enqueueOk, id, err) = await futures.EnqueueCommandAsync(futuresCmd, ct).ConfigureAwait(false);
        if (!enqueueOk)
        {
            return (false, err);
        }

        var (waitOk, body) = await futures.WaitResultAsync(
            id,
            timeoutSeconds: IsCloseAction(tradingCmd.Action) ? 75 : 20,
            ct: ct).ConfigureAwait(false);
        if (!waitOk)
        {
            return (false, body);
        }

        return ParseResultBody(body);
    }

    private static bool IsCloseAction(string action) =>
        action.Equals("close_position", StringComparison.OrdinalIgnoreCase)
        || action.Equals("close_all_positions", StringComparison.OrdinalIgnoreCase);

    private static (bool Ok, string Detail) ParseResultBody(string body)
    {
        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<FuturesPlatformCommandResultFile>(
                body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result is not null)
            {
                return (result.Success, result.Message);
            }
        }
        catch
        {
            // fall through
        }

        return (true, body);
    }

    private static string? MapAction(string action) =>
        action.ToLowerInvariant() switch
        {
            "place_order" => "place_order",
            "cancel_order" => "cancel_order",
            "close_position" => "close_position",
            "close_all_positions" => "close_all_positions",
            "set_leverage" => "set_leverage",
            _ => null,
        };

    private static string? NormalizeSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return null;
        }

        return side.Equals("Sell", StringComparison.OrdinalIgnoreCase) ? "SELL" : "BUY";
    }

    private static string? NormalizeOrderType(string? orderType)
    {
        if (string.IsNullOrWhiteSpace(orderType))
        {
            return "MARKET";
        }

        return orderType.Equals("Limit", StringComparison.OrdinalIgnoreCase) ? "LIMIT" : "MARKET";
    }
}
