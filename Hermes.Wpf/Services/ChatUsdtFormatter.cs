using System.Globalization;

namespace Hermes.Wpf.Services;

/// <summary>USDT amounts for Hermes chat — trims trailing zeros after decimal point.</summary>
internal static class ChatUsdtFormatter
{
    public static string Format(decimal usdt) => Format((double)usdt);

    public static string Format(double usdt)
    {
        var text = usdt.ToString("F2", CultureInfo.InvariantCulture);
        if (text.Contains('.'))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return $"{text} USDT";
    }

    public static string FormatPnl(double pnl)
    {
        var sign = pnl >= 0 ? "+" : "-";
        var abs = Math.Abs(pnl);
        var body = Format(abs);
        return pnl >= 0 ? $"+{body}" : $"-{body}";
    }
}
