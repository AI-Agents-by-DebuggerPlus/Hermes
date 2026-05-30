namespace Hermes.BinanceDemoFuturesTerminal.Models;

public enum FuturesMarginType
{
    Cross,
    Isolated,
}

public static class FuturesMarginTypeExtensions
{
    public static FuturesMarginType ParseApi(string? value) =>
        value?.Equals("isolated", StringComparison.OrdinalIgnoreCase) == true
            ? FuturesMarginType.Isolated
            : FuturesMarginType.Cross;

    public static string ToApiValue(this FuturesMarginType mode) =>
        mode == FuturesMarginType.Isolated ? "ISOLATED" : "CROSSED";

    public static string ToButtonLabel(this FuturesMarginType mode) =>
        mode == FuturesMarginType.Isolated ? "Изолир." : "Кросс";

    public static string ToMarginLabel(this FuturesMarginType mode) =>
        mode == FuturesMarginType.Isolated ? "Изолир." : "Кросс";
}
