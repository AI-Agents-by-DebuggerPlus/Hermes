using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

internal static class SpotTradingCommandExecutor
{
    public static async Task<(bool Ok, string Detail)> ExecuteAsync(
        SpotTerminalBridgeService spot,
        TradingPlatformCommand tradingCmd,
        CancellationToken ct = default)
    {
        if (!await spot.EnsureTerminalReadyAsync(force: true, ct: ct).ConfigureAwait(false))
        {
            return (false, "Hermes.SpotTerminal.exe не запущен. Нажмите SpotTerminal или включите автозапуск.");
        }

        var action = MapAction(tradingCmd.Action);
        if (action is null)
        {
            return (false, $"Действие не поддерживается в SpotTerminal: {tradingCmd.Action}");
        }

        var spotCmd = new SpotPlatformCommand
        {
            Action = action,
            Symbol = tradingCmd.Symbol,
            Side = tradingCmd.Side,
            OrderType = tradingCmd.OrderType,
            Quantity = tradingCmd.Quantity,
            Price = tradingCmd.Price > 0 ? tradingCmd.Price : null,
            OrderId = tradingCmd.OrderId,
            RequestedBy = tradingCmd.RequestedBy ?? "Hermes.Wpf",
        };

        var (enqueueOk, id, err) = await spot.EnqueueCommandAsync(spotCmd, ct).ConfigureAwait(false);
        if (!enqueueOk)
        {
            return (false, err);
        }

        var (waitOk, body) = await spot.WaitResultAsync(id, ct: ct).ConfigureAwait(false);
        if (!waitOk)
        {
            return (false, body);
        }

        return ParseResultBody(body);
    }

    private static (bool Ok, string Detail) ParseResultBody(string body)
    {
        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<SpotPlatformCommandResultFile>(
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
            "set_mode" => "set_mode",
            "close_position" => "place_order",
            "enable_strategy" or "emergency_stop" => null,
            _ => null,
        };
}
