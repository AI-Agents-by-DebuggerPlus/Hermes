using System.Globalization;
using Hermes.BinanceDemoFuturesTerminal.Models;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

internal static class OrderVolumeUsdtHelper
{
    public static double ResolveNotionalUsdt(
        double? quantityUsdt,
        double? quantityContracts,
        double price,
        PlatformSettings settings)
    {
        if (quantityUsdt is > 0)
        {
            return quantityUsdt.Value;
        }

        if (quantityContracts is > 0 && price > 0)
        {
            return quantityContracts.Value * price;
        }

        return settings.DefaultAgentOrderUsdt > 0 ? settings.DefaultAgentOrderUsdt : 50;
    }

    public static double CapNotionalUsdt(
        double notionalUsdt,
        PlatformSettings settings,
        double walletBalanceUsdt,
        int leverage)
    {
        if (!settings.RiskManagementEnabled)
        {
            return notionalUsdt;
        }

        var maxNotional = RiskManager.ComputeMaxOrderNotionalUsdt(walletBalanceUsdt, leverage, settings);
        if (maxNotional > 0 && maxNotional < double.MaxValue)
        {
            return Math.Min(notionalUsdt, maxNotional);
        }

        return notionalUsdt;
    }

    public static bool TryResolveContracts(
        SymbolInfo? symbolInfo,
        double notionalUsdt,
        double price,
        out double qty,
        out string qtyText,
        out string error)
    {
        qty = 0;
        qtyText = string.Empty;
        error = string.Empty;

        if (symbolInfo is null)
        {
            error = "Symbol info not loaded.";
            return false;
        }

        if (price <= 0)
        {
            error = "Price unavailable for USDT→contracts conversion.";
            return false;
        }

        qty = symbolInfo.EnsureMinNotionalQuantity(notionalUsdt / price, price);
        if (qty <= 0)
        {
            error = "Could not resolve contract quantity.";
            return false;
        }

        var minQty = symbolInfo.GetMinQty();
        if (qty + 1e-12 < minQty)
        {
            error = $"Минимальный номинал для {symbolInfo.Symbol}: {symbolInfo.GetMinOrderUsdt(price):F2} USDT.";
            return false;
        }

        qtyText = symbolInfo.FormatQuantity(qty);
        return true;
    }

    public static string FormatNotionalUsdt(double notionalUsdt)
    {
        var text = notionalUsdt.ToString("F2", CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return $"{text} USDT";
    }

    public static string FormatSignedPnl(double pnl)
    {
        var sign = pnl >= 0 ? "+" : "-";
        var body = FormatNotionalUsdt(Math.Abs(pnl)).Replace(" USDT", string.Empty, StringComparison.Ordinal);
        return $"{sign}{body} USDT";
    }

    public static string FormatCloseResult(
        string actionSummary,
        double notionalUsdt,
        double? realizedPnlUsdt,
        bool success,
        string? perSymbolPnl = null)
    {
        var baseText = $"{actionSummary} · {FormatNotionalUsdt(notionalUsdt)}";
        if (realizedPnlUsdt.HasValue)
        {
            baseText += $" · Реализованный PnL: {FormatSignedPnl(realizedPnlUsdt.Value)}";
            if (!string.IsNullOrWhiteSpace(perSymbolPnl))
            {
                baseText += $" ({perSymbolPnl})";
            }
        }

        return success ? baseText : $"Ошибка: {baseText}";
    }

    public static string FormatOrderLog(
        string side,
        string orderType,
        string symbol,
        double notionalUsdt,
        string? priceText = null)
    {
        var pricePart = string.IsNullOrWhiteSpace(priceText) ? "MARKET" : priceText;
        return $"Отправка ордера: {side} {orderType} {symbol} · {FormatNotionalUsdt(notionalUsdt)} @ {pricePart}";
    }

    public static string FormatBridgeResult(
        string actionSummary,
        double notionalUsdt,
        bool success,
        string? extra = null)
    {
        var baseText = $"{actionSummary} · {FormatNotionalUsdt(notionalUsdt)}";
        if (!string.IsNullOrWhiteSpace(extra))
        {
            baseText += $" · {extra}";
        }

        return success ? baseText : $"Ошибка: {baseText}";
    }
}
