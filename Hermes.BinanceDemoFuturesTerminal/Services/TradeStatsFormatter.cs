using System.Globalization;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public static class TradeStatsFormatter
{
    public static string FormatSignedPnl(double pnl)
    {
        if (Math.Abs(pnl) < 1e-12)
        {
            return "0 USDT";
        }

        var body = TrimDecimal(Math.Abs(pnl));
        return pnl >= 0 ? $"+{body} USDT" : $"-{body} USDT";
    }

    public static string FormatCommission(double commission) =>
        $"{TrimDecimal(Math.Abs(commission))} USDT";

    private static string TrimDecimal(double value)
    {
        var text = value.ToString("F2", CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text;
    }
}
