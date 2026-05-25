using System.Globalization;
using System.Text.RegularExpressions;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

internal enum ManualOrderSide
{
    None,
    Buy,
    Sell,
}

internal enum ManualPriceKind
{
    Unspecified,
    Market,
    Limit,
    Stop,
}

internal readonly record struct ManualPriceSpec(ManualPriceKind Kind, decimal Price);

internal sealed class ManualOrderDraft
{
    public required string Symbol { get; init; }
    public required ManualOrderSide Side { get; init; }
    public required decimal Quantity { get; init; }
}

internal static class TradingManualOrderParser
{
    private static readonly Regex OpenVerbPattern = new(
        @"\b(открой|open|купи|buy|продай|sell|выставь|поставь|place)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LongPattern = new(
        @"\b(лонг|long)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ShortPattern = new(
        @"\b(шорт|short)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MarketPricePattern = new(
        @"\b(рыночн\w*|market|маркет\w*|по\s+рынк\w*|mkt)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LimitKeywordPattern = new(
        @"\b(лимит\w*|limit)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StopKeywordPattern = new(
        @"\b(стоп\w*|stop)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex QuantityPattern = new(
        @"\b(\d+(?:[.,]\d+)?)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CancelPattern = new(
        @"^\s*(нет|no|отмен\w*|cancel)\s*\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsCancel(string? text) =>
        !string.IsNullOrWhiteSpace(text) && CancelPattern.IsMatch(text.Trim());

    public static bool TryParseOpenRequest(string text, IEnumerable<string>? knownSymbols, out ManualOrderDraft? draft, out ManualPriceSpec price)
    {
        draft = null;
        price = new ManualPriceSpec(ManualPriceKind.Unspecified, 0);

        if (string.IsNullOrWhiteSpace(text) || TradingClosePositionTriggers.Matches(text))
        {
            return false;
        }

        var t = text.Trim();
        if (!LooksLikeOpenOrderIntent(t))
        {
            return false;
        }

        var side = ResolveSide(t);
        if (side == ManualOrderSide.None)
        {
            return false;
        }

        var symbol = TradingSymbolResolver.ResolveFromText(t, knownSymbols);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var qty = TryParseQuantity(t) ?? DefaultQuantity(symbol);
        price = ParsePriceSpec(t);
        draft = new ManualOrderDraft
        {
            Symbol = symbol,
            Side = side,
            Quantity = qty,
        };
        return true;
    }

    public static bool TryParsePriceOnly(string text, out ManualPriceSpec price)
    {
        price = ParsePriceSpec(text.Trim());
        return price.Kind != ManualPriceKind.Unspecified;
    }

    public static TradingPlatformCommand BuildCommand(ManualOrderDraft draft, ManualPriceSpec price)
    {
        var orderType = price.Kind switch
        {
            ManualPriceKind.Limit => "Limit",
            ManualPriceKind.Stop => "Stop",
            _ => "Market",
        };

        var cmdPrice = orderType == "Market" ? 0m : price.Price;
        return new TradingPlatformCommand
        {
            Action = "place_order",
            Symbol = draft.Symbol,
            Side = draft.Side == ManualOrderSide.Buy ? "Buy" : "Sell",
            OrderType = orderType,
            Quantity = draft.Quantity,
            Price = cmdPrice,
            ReduceOnly = false,
            RequestedBy = "Hermes.Wpf-manual",
        };
    }

    public static string FormatSideRu(ManualOrderSide side) =>
        side == ManualOrderSide.Buy ? "лонг (Buy)" : "шорт (Sell)";

    private static bool LooksLikeOpenOrderIntent(string text)
    {
        if (OpenVerbPattern.IsMatch(text))
        {
            return true;
        }

        var hasLong = LongPattern.IsMatch(text);
        var hasShort = ShortPattern.IsMatch(text);
        return hasLong ^ hasShort;
    }

    private static ManualOrderSide ResolveSide(string text)
    {
        var hasLong = LongPattern.IsMatch(text);
        var hasShort = ShortPattern.IsMatch(text);
        if (hasLong && !hasShort)
        {
            return ManualOrderSide.Buy;
        }

        if (hasShort && !hasLong)
        {
            return ManualOrderSide.Sell;
        }

        if (Regex.IsMatch(text, @"\b(купи|buy)\b", RegexOptions.IgnoreCase))
        {
            return ManualOrderSide.Buy;
        }

        if (Regex.IsMatch(text, @"\b(продай|sell)\b", RegexOptions.IgnoreCase) && !hasLong)
        {
            return ManualOrderSide.Sell;
        }

        return ManualOrderSide.None;
    }

    private static ManualPriceSpec ParsePriceSpec(string text)
    {
        if (MarketPricePattern.IsMatch(text))
        {
            return new ManualPriceSpec(ManualPriceKind.Market, 0);
        }

        var isLimit = LimitKeywordPattern.IsMatch(text);
        var isStop = StopKeywordPattern.IsMatch(text);
        var numeric = TryParsePriceNumber(text);
        if (numeric is > 0)
        {
            return isStop
                ? new ManualPriceSpec(ManualPriceKind.Stop, numeric.Value)
                : new ManualPriceSpec(ManualPriceKind.Limit, numeric.Value);
        }

        if (isStop || isLimit)
        {
            return new ManualPriceSpec(ManualPriceKind.Unspecified, 0);
        }

        return new ManualPriceSpec(ManualPriceKind.Unspecified, 0);
    }

    private static decimal? TryParsePriceNumber(string text)
    {
        var matches = Regex.Matches(text, @"\b(\d{2,8}(?:[.,]\d+)?)\b");
        decimal? best = null;
        foreach (Match m in matches)
        {
            var raw = m.Groups[1].Value.Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            if (v is < 10 or > 10_000_000)
            {
                continue;
            }

            best = v;
        }

        return best;
    }

    private static decimal? TryParseQuantity(string text)
    {
        foreach (Match m in QuantityPattern.Matches(text))
        {
            var raw = m.Groups[1].Value.Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            if (v is > 0 and < 1000)
            {
                return v;
            }
        }

        return null;
    }

    private static decimal DefaultQuantity(string symbol) =>
        symbol.ToUpperInvariant() switch
        {
            "BTCUSDT" => 0.01m,
            "ETHUSDT" => 0.05m,
            "SOLUSDT" => 1m,
            "BNBUSDT" => 0.1m,
            _ => 0.01m,
        };
}
