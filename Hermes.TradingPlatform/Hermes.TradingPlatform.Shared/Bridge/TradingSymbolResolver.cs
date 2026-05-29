namespace Hermes.TradingPlatform.Shared.Bridge;

public static class TradingSymbolResolver
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ETH"] = "ETHUSDT",
        ["BTC"] = "BTCUSDT",
        ["SOL"] = "SOLUSDT",
        ["BNB"] = "BNBUSDT",
        ["XRP"] = "XRPUSDT",
        ["DOGE"] = "DOGEUSDT",
        ["ADA"] = "ADAUSDT",
        ["эфир"] = "ETHUSDT",
        ["эфиру"] = "ETHUSDT",
        ["биткоин"] = "BTCUSDT",
        ["битка"] = "BTCUSDT",
        ["битку"] = "BTCUSDT",
        ["битке"] = "BTCUSDT",
        ["биткон"] = "BTCUSDT",
        ["биткоину"] = "BTCUSDT",
    };

    public static string? Resolve(string? input, IEnumerable<string>? knownSymbols = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var t = input.Trim();
        if (Aliases.TryGetValue(t, out var alias))
        {
            t = alias;
        }

        t = t.ToUpperInvariant();
        if (knownSymbols is not null)
        {
            var hit = knownSymbols.FirstOrDefault(s =>
                string.Equals(s, t, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit;
            }
        }

        if (t.EndsWith("USDT", StringComparison.Ordinal))
        {
            return t;
        }

        return $"{t}USDT";
    }

    /// <summary>Find symbol from free text like «закрой позицию по эфиру».</summary>
    public static string? ResolveFromText(string text, IEnumerable<string>? knownSymbols = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lower = text.ToLowerInvariant();
        foreach (var pair in Aliases)
        {
            if (lower.Contains(pair.Key, StringComparison.Ordinal))
            {
                return Resolve(pair.Value, knownSymbols);
            }
        }

        foreach (var sym in knownSymbols ?? [])
        {
            if (lower.Contains(sym, StringComparison.OrdinalIgnoreCase))
            {
                return sym;
            }
        }

        var usdtMatch = System.Text.RegularExpressions.Regex.Match(
            lower,
            @"\b([a-z]{2,15}usdt)\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (usdtMatch.Success)
        {
            return Resolve(usdtMatch.Groups[1].Value, knownSymbols);
        }

        return null;
    }
}
