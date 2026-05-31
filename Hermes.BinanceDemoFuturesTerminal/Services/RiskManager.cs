using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class RiskManager
{
    public static double GetWalletBalanceUsdt(IEnumerable<BalanceModel> balances) =>
        balances.FirstOrDefault(b => b.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))?.Total ?? 0;

    public static double ComputeMaxOrderMarginUsdt(double walletBalanceUsdt, PlatformSettings settings)
    {
        if (!settings.RiskManagementEnabled || settings.MaxOrderMarginPercent <= 0)
        {
            return double.MaxValue;
        }

        return walletBalanceUsdt * settings.MaxOrderMarginPercent / 100.0;
    }

    public static double ComputeMaxOrderNotionalUsdt(
        double walletBalanceUsdt,
        int leverage,
        PlatformSettings settings)
    {
        var maxMargin = ComputeMaxOrderMarginUsdt(walletBalanceUsdt, settings);
        if (maxMargin <= 0 || double.IsPositiveInfinity(maxMargin))
        {
            return double.MaxValue;
        }

        return maxMargin * Math.Max(leverage, 1);
    }

    public static double EstimateOrderMarginUsdt(double orderNotionalUsdt, int leverage) =>
        orderNotionalUsdt / Math.Max(leverage, 1);

    public static string? ValidateOrder(
        PlatformSettings settings,
        double orderNotionalUsdt,
        double walletBalanceUsdt,
        int orderLeverage,
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

        if (settings.MaxOrderMarginPercent > 0 && walletBalanceUsdt > 0)
        {
            var orderMargin = EstimateOrderMarginUsdt(orderNotionalUsdt, orderLeverage);
            var maxMargin = ComputeMaxOrderMarginUsdt(walletBalanceUsdt, settings);
            if (orderMargin > maxMargin + 1e-8)
            {
                return
                    $"Риск: маржа ордера {orderMargin:N2} USDT превышает лимит {maxMargin:N2} USDT "
                    + $"({settings.MaxOrderMarginPercent.ToString("0.##")}% от депозита {walletBalanceUsdt:N2} USDT).";
            }
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
