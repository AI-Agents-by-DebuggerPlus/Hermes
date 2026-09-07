using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public static class TradingAnalyticsSignalExecutor
{
    public static Mt5TerminalRouteCommand ToMt5Command(TradingAnalyticsSignalCard card) =>
        new()
        {
            Action = "place_pending",
            Id = card.SignalId,
            Symbol = card.Symbol,
            PendingOrderType = card.PendingOrderTypeLabel,
            Price = card.EffectiveEntry,
            StopLoss = card.StopLoss,
            TakeProfit = card.TakeProfit,
            Lot = card.Lot,
        };
}
