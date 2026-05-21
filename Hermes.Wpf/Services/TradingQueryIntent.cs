namespace Hermes.Wpf.Services;

public enum TradingQueryIntent
{
    None,
    BalanceOnly,
    AccountSummary,
}

/// <summary>Classifies trader status questions for scoped replies (balance vs full account).</summary>
public static class TradingQueryIntentClassifier
{
    private static readonly string[] SummaryMarkers =
    [
        "сводк",
        "состояние счет",
        "состояние счёт",
        "статус счет",
        "статус счёт",
        "account summary",
        "обзор счет",
        "обзор счёт",
        "детальн",
        "полная информация",
        "полный отчет",
        "полный отчёт",
        "информация по счет",
        "информация по счёт",
        "сводка по счет",
        "сводка по счёт",
    ];

    private static readonly string[] BalanceMarkers =
    [
        "текущий баланс",
        "баланс",
        "balance",
        "сколько на счет",
        "сколько на счёт",
        "остаток на счет",
        "остаток на счёт",
    ];

    private static readonly string[] BalanceOnlyExclusions =
    [
        "позици",
        "position",
        "ордер",
        "order",
        "сводк",
        "equity",
        "марж",
        "margin",
        "pnl",
        "прибыл",
        "убыт",
        "риск",
        "стратег",
        "просадк",
        "drawdown",
        "оркестр",
    ];

    public static TradingQueryIntent Classify(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return TradingQueryIntent.None;
        }

        var t = message.Trim().ToLowerInvariant();

        foreach (var marker in SummaryMarkers)
        {
            if (t.Contains(marker, StringComparison.Ordinal))
            {
                return TradingQueryIntent.AccountSummary;
            }
        }

        foreach (var marker in BalanceMarkers)
        {
            if (!t.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var ex in BalanceOnlyExclusions)
            {
                if (t.Contains(ex, StringComparison.Ordinal))
                {
                    return TradingQueryIntent.None;
                }
            }

            return TradingQueryIntent.BalanceOnly;
        }

        return TradingQueryIntent.None;
    }
}
