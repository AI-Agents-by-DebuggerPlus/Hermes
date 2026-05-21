using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Heuristic: user message looks like a trading-platform task (normal mode → ask to switch).</summary>
public static class TradingTaskDetector
{
    private static readonly Regex SymbolRegex = new(
        @"\b(BTC|ETH|BNB|SOL|XRP|DOGE|ADA|AVAX|LINK|DOT|MATIC)[A-Z]{0,6}USDT\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] Keywords =
    [
        "трейдинг", "trading", "бирж", "фьючерс", "futures", "спот", "spot",
        "позици", "position", "ордер", "order", "лимитк", "маркет", "market order",
        "лонг", "long", "шорт", "short", "стоп", "stop loss", "тейк", "take profit",
        "баланс", "balance", "equity", "марж", "margin", "плеч", "leverage",
        "pnl", "прибыл", "убыт", "drawdown", "просадк", "риск", "safe mode",
        "стратег", "strategy", "momentum", "mean-rev", "mean rev", "liq-sweep",
        "btcusdt", "ethusdt", "hermes trading", "trading platform", "терминал",
        "отложен", "pending order", "отмени ордер", "cancel order", "emergency",
    ];

    public static bool IsTradingRelated(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (TradingModeTriggers.MatchesEnable(message)
            || TradingModeTriggers.MatchesAgentMode(message)
            || TradingModeTriggers.MatchesConfirmYes(message)
            || TradingModeTriggers.MatchesConfirmNo(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();
        if (SymbolRegex.IsMatch(message))
        {
            return true;
        }

        foreach (var kw in Keywords)
        {
            if (t.Contains(kw, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
