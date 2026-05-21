using System.Globalization;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Wpf.Services;

internal static class ManualTradeNotifier
{
    public static bool TryParseQuantity(string? text, out decimal quantity)
    {
        quantity = 0m;
        return !string.IsNullOrWhiteSpace(text)
               && decimal.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out quantity)
               && quantity > 0;
    }

    public static decimal ResolveMarketPrice(TradingReadModel readModel, string symbol)
    {
        var ticker = readModel.GetMarketWatch().FirstOrDefault(t =>
            string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        return ticker?.Price ?? 0m;
    }

    public static void ReportOrder(Order order, string context) =>
        TradeUiFeedback.Instance.ReportOrder(order, context);

    public static void ReportWarning(string message) =>
        TradeUiFeedback.Instance.ReportWarning(message);

    public static void ReportInfo(string message) =>
        TradeUiFeedback.Instance.ReportInfo(message);
}
