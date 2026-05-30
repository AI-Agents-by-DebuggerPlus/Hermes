using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class RiskManager
{
    public static string? ValidateOrder(
        PlatformSettings settings,
        double orderNotionalUsdt,
        IReadOnlyList<PositionModel> openPositions,
        string symbol,
        bool isNewPosition)
    {
        if (!settings.RiskManagementEnabled)
        {
            return null;
        }

        if (orderNotionalUsdt <= 0)
        {
            return "Номинал ордера должен быть больше 0 USDT.";
        }

        if (settings.MaxOrderUsdt > 0 && orderNotionalUsdt > settings.MaxOrderUsdt)
        {
            return $"Риск: номинал ордера {orderNotionalUsdt:N2} USDT превышает лимит {settings.MaxOrderUsdt:N2} USDT.";
        }

        var currentExposure = openPositions.Sum(p => Math.Abs(p.Size) * p.MarkPrice);
        var projected = currentExposure + orderNotionalUsdt;

        if (settings.MaxTotalExposureUsdt > 0 && projected > settings.MaxTotalExposureUsdt)
        {
            return $"Риск: суммарная экспозиция {projected:N2} USDT превысит лимит {settings.MaxTotalExposureUsdt:N2} USDT.";
        }

        if (isNewPosition && settings.MaxOpenPositions > 0)
        {
            var hasSymbol = openPositions.Any(p =>
                p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (!hasSymbol && openPositions.Count >= settings.MaxOpenPositions)
            {
                return $"Риск: уже {openPositions.Count} открытых позиций (макс. {settings.MaxOpenPositions}).";
            }
        }

        var symbolPosition = openPositions.FirstOrDefault(p =>
            p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        if (symbolPosition != null && settings.MaxLeverage > 0 && symbolPosition.Leverage > settings.MaxLeverage)
        {
            return $"Риск: плечо по {symbol} ({symbolPosition.Leverage}x) выше лимита {settings.MaxLeverage}x.";
        }

        return null;
    }

    public static double EstimateNotionalUsdt(double contractQty, double price) =>
        contractQty * price;

    public static int CapLeverage(int exchangeLeverage, int maxLeverage) =>
        maxLeverage > 0 ? Math.Min(exchangeLeverage, maxLeverage) : exchangeLeverage;

    public static string? ValidateSymbolLeverage(PlatformSettings settings, int exchangeLeverage)
    {
        if (!settings.RiskManagementEnabled || settings.MaxLeverage <= 0)
        {
            return null;
        }

        if (exchangeLeverage > settings.MaxLeverage)
        {
            return $"Риск: плечо {exchangeLeverage}x превышает лимит {settings.MaxLeverage}x из риск-менеджера.";
        }

        return null;
    }
}
