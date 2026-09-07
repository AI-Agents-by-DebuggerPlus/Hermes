using System.Globalization;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Test order card built from live chart quote (relative entry / SL / TP).</summary>
public sealed class Mt5TerminalTestOrderCard
{
    public required string Title { get; init; }
    public required string KindLabel { get; init; }
    public required bool IsMarket { get; init; }
    public required bool IsBuy { get; init; }
    public required string Symbol { get; init; }
    public double? Entry { get; init; }
    public double? StopLoss { get; init; }
    public double? TakeProfit { get; init; }
    public required double Lot { get; init; }
    public string? PendingOrderType { get; init; }
    public required string Summary { get; init; }
    public required Mt5TerminalRouteCommand Command { get; init; }

    public string EntryText =>
        IsMarket
            ? "market"
            : (Entry?.ToString("G", CultureInfo.InvariantCulture) ?? "—");

    public string SlText => StopLoss?.ToString("G", CultureInfo.InvariantCulture) ?? "—";
    public string TpText => TakeProfit?.ToString("G", CultureInfo.InvariantCulture) ?? "—";
    public string SideText => IsBuy ? "BUY" : "SELL";
}

public static class Mt5TerminalTestOrderCatalog
{
    public const double DefaultLot = 0.01;

    /// <summary>
    /// Relative offsets from mid: entry gap, SL distance, TP distance.
    /// </summary>
    public const double EntryGapFraction = 0.0008; // 0.08%
    public const double SlFraction = 0.0015;       // 0.15%
    public const double TpFraction = 0.0025;       // 0.25%

    public static IReadOnlyList<Mt5TerminalTestOrderCard> Build(Mt5TerminalQuote quote, double lot = DefaultLot)
    {
        if (quote.Mid <= 0 || string.IsNullOrWhiteSpace(quote.Symbol))
        {
            return Array.Empty<Mt5TerminalTestOrderCard>();
        }

        var mid = quote.Mid;
        var gap = Math.Max(quote.Round(mid * EntryGapFraction), MinTick(quote));
        var slDist = Math.Max(quote.Round(mid * SlFraction), gap * 2);
        var tpDist = Math.Max(quote.Round(mid * TpFraction), gap * 3);
        var cards = new List<Mt5TerminalTestOrderCard>(8);

        cards.Add(Market("Buy Market", buy: true, quote, lot));
        cards.Add(Market("Sell Market", buy: false, quote, lot));

        // Buy Limit: entry below market
        cards.Add(Pending(
            "Buy Limit",
            buy: true,
            orderType: "limit",
            entry: quote.Round(mid - gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Лимит ниже рынка"));

        // Sell Limit: entry above market
        cards.Add(Pending(
            "Sell Limit",
            buy: false,
            orderType: "limit",
            entry: quote.Round(mid + gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Лимит выше рынка"));

        // Buy Stop: entry above market
        cards.Add(Pending(
            "Buy Stop",
            buy: true,
            orderType: "stop",
            entry: quote.Round(mid + gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Стоп выше рынка"));

        // Sell Stop: entry below market
        cards.Add(Pending(
            "Sell Stop",
            buy: false,
            orderType: "stop",
            entry: quote.Round(mid - gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Стоп ниже рынка"));

        // Buy Stop Limit
        cards.Add(Pending(
            "Buy Stop Limit",
            buy: true,
            orderType: "stop_limit",
            entry: quote.Round(mid + gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Stop Limit выше рынка"));

        // Sell Stop Limit
        cards.Add(Pending(
            "Sell Stop Limit",
            buy: false,
            orderType: "stop_limit",
            entry: quote.Round(mid - gap),
            slDist,
            tpDist,
            quote,
            lot,
            "Stop Limit ниже рынка"));

        return cards;
    }

    private static double MinTick(Mt5TerminalQuote quote) =>
        Math.Pow(10, -Math.Max(1, quote.Digits));

    private static Mt5TerminalTestOrderCard Market(string title, bool buy, Mt5TerminalQuote quote, double lot)
    {
        var action = buy ? "buy_market" : "sell_market";
        return new Mt5TerminalTestOrderCard
        {
            Title = title,
            KindLabel = "Market",
            IsMarket = true,
            IsBuy = buy,
            Symbol = quote.Symbol,
            Lot = lot,
            Summary = $"Рыночный {(buy ? "лонг" : "шорт")} @ {Fmt(quote.Mid)} (лот {lot})",
            Command = new Mt5TerminalRouteCommand
            {
                Action = action,
                Id = Guid.NewGuid().ToString("N"),
                Lot = lot,
                Symbol = quote.Symbol,
            },
        };
    }

    private static Mt5TerminalTestOrderCard Pending(
        string title,
        bool buy,
        string orderType,
        double entry,
        double slDist,
        double tpDist,
        Mt5TerminalQuote quote,
        double lot,
        string note)
    {
        double sl;
        double tp;
        if (buy)
        {
            sl = quote.Round(entry - slDist);
            tp = quote.Round(entry + tpDist);
        }
        else
        {
            sl = quote.Round(entry + slDist);
            tp = quote.Round(entry - tpDist);
        }

        var card = new TradingAnalyticsSignalCard
        {
            SignalId = Guid.NewGuid().ToString("N"),
            Symbol = quote.Symbol,
            Side = buy ? "buy" : "sell",
            OrderType = orderType,
            Entry = entry,
            StopLoss = sl,
            TakeProfit = tp,
            Lot = lot,
            Timeframe = "TEST",
            Summary = note + $" · mid={Fmt(quote.Mid)}",
        };

        return new Mt5TerminalTestOrderCard
        {
            Title = title,
            KindLabel = card.PendingOrderTypeLabel,
            IsMarket = false,
            IsBuy = buy,
            Symbol = quote.Symbol,
            Entry = entry,
            StopLoss = sl,
            TakeProfit = tp,
            Lot = lot,
            PendingOrderType = card.PendingOrderTypeLabel,
            Summary = card.Summary ?? note,
            Command = TradingAnalyticsSignalExecutor.ToMt5Command(card),
        };
    }

    private static string Fmt(double v) => v.ToString("G", CultureInfo.InvariantCulture);
}
