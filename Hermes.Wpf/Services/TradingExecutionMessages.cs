using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

/// <summary>Plain-language chat notifications for trading bridge execution.</summary>
internal static class TradingExecutionMessages
{
    public static string FormatCommandSent(TradingPlatformCommand cmd, bool futuresMarket)
    {
        var venue = futuresMarket ? "Binance Demo Futures" : "Spot Terminal";
        var action = DescribeAction(cmd);
        return $"Команда отправлена на сервер {venue}: {action}.";
    }

    public static string FormatCommandResult(bool ok, string detail, TradingPlatformCommand cmd, bool futuresMarket)
    {
        var venue = futuresMarket ? "Binance Demo Futures" : "Spot Terminal";
        var text = TradingPlatformIntentParser.TryExtractResultMessagePublic(detail) ?? detail.Trim();
        if (string.IsNullOrEmpty(text))
        {
            text = ok ? "команда выполнена" : "нет ответа от терминала";
        }

        if (ok)
        {
            return $"Результат ({venue}): {text}";
        }

        return $"Не удалось выполнить команду ({venue}): {text}";
    }

    private static string DescribeAction(TradingPlatformCommand cmd)
    {
        var symbol = string.IsNullOrWhiteSpace(cmd.Symbol) ? "—" : cmd.Symbol.ToUpperInvariant();
        var volume = FormatVolumeUsdt(cmd);
        return cmd.Action.ToLowerInvariant() switch
        {
            "place_order" =>
                $"{FormatSideRu(cmd.Side)} {FormatOrderTypeRu(cmd.OrderType)} {symbol}"
                + (volume is not null ? $", {volume}" : string.Empty)
                + (cmd.Price is > 0 ? $", цена {FormatPrice(cmd.Price.Value)}" : string.Empty),
            "close_position" =>
                $"закрытие позиции {symbol}"
                + (volume is not null ? $", {volume}" : string.Empty)
                + $" ({FormatOrderTypeRu(cmd.OrderType)})",
            "close_all_positions" => "закрытие всех позиций по рынку",
            "cancel_order" => $"отмена ордера #{cmd.OrderId} {symbol}",
            "set_leverage" => $"установка плеча {cmd.Leverage}x для {symbol}",
            _ => $"{cmd.Action} {symbol}".Trim(),
        };
    }

    private static string? FormatVolumeUsdt(TradingPlatformCommand cmd)
    {
        if (cmd.QuantityUsdt is > 0)
        {
            return ChatUsdtFormatter.Format(cmd.QuantityUsdt.Value);
        }

        if (cmd.Quantity is > 0 && cmd.Price is > 0)
        {
            return ChatUsdtFormatter.Format(cmd.Quantity.Value * cmd.Price.Value);
        }

        return null;
    }

    private static string FormatPrice(decimal price)
    {
        var text = price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text;
    }

    private static string FormatSideRu(string? side) =>
        side?.Equals("Sell", StringComparison.OrdinalIgnoreCase) == true
        || side?.Equals("SELL", StringComparison.OrdinalIgnoreCase) == true
            ? "шорт (SELL)"
            : "лонг (BUY)";

    private static string FormatOrderTypeRu(string? orderType) =>
        orderType?.Equals("Limit", StringComparison.OrdinalIgnoreCase) == true
        || orderType?.Equals("LIMIT", StringComparison.OrdinalIgnoreCase) == true
            ? "лимитный ордер"
            : "рыночный ордер";
}
