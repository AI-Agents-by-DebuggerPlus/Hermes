using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class PositionProtectionHelper
{
    public static bool IsStopLossType(string type) =>
        type.StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
        && !type.StartsWith("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase);

    public static bool IsTakeProfitType(string type) =>
        type.Contains("TAKE_PROFIT", StringComparison.OrdinalIgnoreCase);

    public static bool IsConditionalType(string type) =>
        IsStopLossType(type) || IsTakeProfitType(type);

    public static void ApplyProtectionFromOrders(IList<PositionModel> positions, IEnumerable<BinanceOrder> openOrders)
    {
        foreach (var position in positions)
        {
            position.StopLoss = null;
            position.TakeProfit = null;

            var symbolOrders = openOrders
                .Where(o => o.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var stopOrder = symbolOrders
                .Where(o => IsStopLossType(o.Type))
                .OrderByDescending(o => o.UpdateTime > 0 ? o.UpdateTime : o.Time)
                .FirstOrDefault();
            if (stopOrder != null && TryParseStopPrice(stopOrder, out var sl))
            {
                position.StopLoss = sl;
            }

            var tpOrder = symbolOrders
                .Where(o => IsTakeProfitType(o.Type))
                .OrderByDescending(o => o.UpdateTime > 0 ? o.UpdateTime : o.Time)
                .FirstOrDefault();
            if (tpOrder != null && TryParseStopPrice(tpOrder, out var tp))
            {
                position.TakeProfit = tp;
            }
        }
    }

    public static bool TryParseStopPrice(BinanceOrder order, out double price)
    {
        price = 0;
        if (!string.IsNullOrWhiteSpace(order.StopPrice)
            && double.TryParse(order.StopPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price)
            && price > 0)
        {
            return true;
        }

        return double.TryParse(order.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out price) && price > 0;
    }

    public static bool TryParseOptionalPrice(string? text, out double price)
    {
        price = 0;
        return !string.IsNullOrWhiteSpace(text)
               && double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out price)
               && price > 0;
    }

    public static bool ValidateProtection(bool isLong, double referencePrice, double? stopLoss, double? takeProfit, out string error)
    {
        error = string.Empty;
        if (referencePrice <= 0)
        {
            error = "Нет цены для проверки SL/TP.";
            return false;
        }

        if (stopLoss.HasValue)
        {
            if (isLong && stopLoss.Value >= referencePrice)
            {
                error = "Stop-Loss для LONG должен быть ниже текущей цены.";
                return false;
            }

            if (!isLong && stopLoss.Value <= referencePrice)
            {
                error = "Stop-Loss для SHORT должен быть выше текущей цены.";
                return false;
            }
        }

        if (takeProfit.HasValue)
        {
            if (isLong && takeProfit.Value <= referencePrice)
            {
                error = "Take-Profit для LONG должен быть выше текущей цены.";
                return false;
            }

            if (!isLong && takeProfit.Value >= referencePrice)
            {
                error = "Take-Profit для SHORT должен быть ниже текущей цены.";
                return false;
            }
        }

        return true;
    }
}
