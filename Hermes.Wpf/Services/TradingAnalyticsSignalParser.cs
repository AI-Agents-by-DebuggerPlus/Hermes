using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public static partial class TradingAnalyticsSignalParser
{
    public const string SkillName = "trade_signal";

    public static bool IsTradingAnalyticsProject(string? projectName) =>
        !string.IsNullOrWhiteSpace(projectName)
        && projectName.Trim().Contains("Trading Analytics", StringComparison.OrdinalIgnoreCase);

    public static bool TryParseFromAgentOutput(string? text, out TradingAnalyticsSignalCard card)
    {
        card = null!;
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return false;
        }

        foreach (var json in EnumerateCandidateObjects(raw).Reverse())
        {
            if (TryParseObject(json, out card))
            {
                return true;
            }
        }

        return TryParseFromStructuredText(raw, out card);
    }

    public static bool TryParseObjectFromJsonOnly(string? text, out TradingAnalyticsSignalCard card)
    {
        card = null!;
        var raw = text ?? string.Empty;
        foreach (var json in EnumerateCandidateObjects(raw).Reverse())
        {
            if (TryParseObject(json, out card))
            {
                return true;
            }
        }

        foreach (Match m in JsonFenceRegex().Matches(raw).Cast<Match>().Reverse())
        {
            if (TryParseObject(m.Groups[1].Value, out card))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fallback when agent prints Entry/SL/TP in markdown but omits <c>skill=trade_signal</c> JSON.
    /// </summary>
    public static bool TryParseFromStructuredText(string? text, out TradingAnalyticsSignalCard card)
    {
        card = null!;
        var raw = text ?? string.Empty;
        if (raw.Length < 40)
        {
            return false;
        }

        var orderMatch = PendingOrderTypeRegex().Match(raw);
        if (!orderMatch.Success)
        {
            return false;
        }

        var pendingLabel = orderMatch.Groups[1].Value.Trim();
        var isBuy = pendingLabel.StartsWith("Buy", StringComparison.OrdinalIgnoreCase);
        var orderType = pendingLabel.Contains("Stop Limit", StringComparison.OrdinalIgnoreCase)
            ? "stop_limit"
            : pendingLabel.Contains("Stop", StringComparison.OrdinalIgnoreCase)
                ? "stop"
                : "limit";

        var symbol = MatchSymbol(raw);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        if (!TryMatchPrice(raw, EntryPriceRegex(), out var entry)
            && !TryMatchPrice(raw, InstructionPriceRegex(), out entry))
        {
            return false;
        }

        TryMatchPrice(raw, StopLossRegex(), out var sl);
        TryMatchPrice(raw, TakeProfitRegex(), out var tp);
        if (sl <= 0 && tp <= 0)
        {
            return false;
        }

        double? lot = null;
        if (TryMatchPrice(raw, LotRegex(), out var lotVal) && lotVal > 0)
        {
            lot = lotVal;
        }

        var summaryMatch = SignalHeadingRegex().Match(raw);
        var summary = summaryMatch.Success
            ? summaryMatch.Groups[1].Value.Trim()
            : $"{pendingLabel} {symbol}";

        card = new TradingAnalyticsSignalCard
        {
            SignalId = Guid.NewGuid().ToString("N"),
            Symbol = symbol,
            Side = isBuy ? "buy" : "sell",
            OrderType = orderType,
            Entry = entry,
            StopLoss = sl > 0 ? sl : null,
            TakeProfit = tp > 0 ? tp : null,
            Lot = lot,
            Summary = summary,
        };
        return true;
    }

    private static string? MatchSymbol(string raw)
    {
        foreach (Match m in SymbolRegex().Matches(raw))
        {
            var fromLine = ExtractSymbolToken(m.Groups[1].Value);
            if (IsPlausibleTradeSymbol(fromLine))
            {
                return fromLine;
            }
        }

        var heading = SignalHeadingRegex().Match(raw);
        if (heading.Success)
        {
            var fromHeading = ExtractSymbolToken(heading.Groups[1].Value);
            if (IsPlausibleTradeSymbol(fromHeading))
            {
                return fromHeading;
            }
        }

        return null;
    }

    private static bool IsPlausibleTradeSymbol(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var s = symbol.Trim().ToUpperInvariant();
        if (s.Length < 3)
        {
            return false;
        }

        return s switch
        {
            "MT5" or "MT4" or "METATRADER" or "TERMINAL" or "TOOLBOX" or "TRADE" or "ORDER"
                or "PLACE" or "LIMIT" or "PENDING" or "MARKET" or "SYMBOL" or "INSTRUMENT" => false,
            _ => true,
        };
    }

    private static string? ExtractSymbolToken(string text)
    {
        var part = (text ?? string.Empty).Trim();
        if (part.Length == 0)
        {
            return null;
        }

        var beforeParen = part.Split('(')[0].Trim();
        var token = SymbolTokenRegex().Match(beforeParen);
        if (!token.Success)
        {
            return null;
        }

        var sym = token.Groups[1].Value.Trim().ToUpperInvariant();
        return IsPlausibleTradeSymbol(sym) ? sym : null;
    }

    private static bool TryMatchPrice(string raw, Regex regex, out double value)
    {
        value = 0;
        var m = regex.Match(raw);
        if (!m.Success)
        {
            return false;
        }

        return TryParsePriceToken(m.Groups[1].Value, out value);
    }

    private static bool TryParsePriceToken(string token, out double value)
    {
        value = 0;
        var t = (token ?? string.Empty).Trim().Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return double.TryParse(t, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value)
               && value > 0;
    }

    public static string StripSignalJsonFromDisplay(string? text)
    {
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return raw;
        }

        var result = raw;
        foreach (Match m in JsonFenceRegex().Matches(raw).Cast<Match>().Reverse())
        {
            if (TryParseObject(m.Groups[1].Value, out _))
            {
                result = result.Remove(m.Index, m.Length);
            }
        }

        foreach (var json in EnumerateCandidateObjects(raw).Reverse())
        {
            if (!TryParseObject(json, out _))
            {
                continue;
            }

            var idx = result.LastIndexOf(json, StringComparison.Ordinal);
            if (idx >= 0)
            {
                result = result.Remove(idx, json.Length);
            }
        }

        return CollapseBlankLines(result.Trim());
    }

    public static bool TryParseObject(string json, out TradingAnalyticsSignalCard card)
    {
        card = null!;
        json = (json ?? string.Empty).Trim();
        if (json.Length < 10 || json[0] != '{')
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!HasTradeSignalSkill(root))
            {
                return false;
            }

            var symbol = ReadString(root, "symbol");
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            var side = ReadString(root, "side") ?? "buy";
            var orderType = ReadString(root, "order_type") ?? "limit";
            if (!TryReadDouble(root, "entry", out var entry) && !TryReadDouble(root, "price", out entry))
            {
                return false;
            }

            card = new TradingAnalyticsSignalCard
            {
                SignalId = ReadString(root, "signal_id") ?? Guid.NewGuid().ToString("N"),
                Symbol = symbol.Trim(),
                Side = side.Trim(),
                OrderType = orderType.Trim(),
                Entry = entry,
                EntryHigh = TryReadDouble(root, "entry_high", out var eh) ? eh : null,
                StopLoss = TryReadDouble(root, "stop_loss", out var sl) ? sl : null,
                TakeProfit = TryReadDouble(root, "take_profit", out var tp) ? tp : null,
                Lot = TryReadDouble(root, "lot", out var lot) ? lot : null,
                Timeframe = ReadString(root, "timeframe"),
                Summary = ReadString(root, "summary") ?? ReadString(root, "rationale"),
                VisualizationPath = ReadString(root, "visualization"),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasTradeSignalSkill(JsonElement root)
    {
        var skill = ReadString(root, "skill");
        return string.Equals(skill, SkillName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static bool TryReadDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var el))
        {
            return false;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
        {
            return true;
        }

        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateObjects(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var j = i; j < text.Length; j++)
            {
                var ch = text[j];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (ch == '\\')
                    {
                        escape = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var block = text.Substring(i, j - i + 1);
                        if (block.Contains("trade_signal", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return block;
                        }

                        break;
                    }
                }
            }
        }
    }

    private static string CollapseBlankLines(string text)
    {
        while (text.Contains("\n\n\n", StringComparison.Ordinal))
        {
            text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return text;
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"\b((?:Buy|Sell)\s+Limit(?:\s+Order)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PendingOrderTypeRegex();

    [GeneratedRegex(@"(?:Инструмент|Instrument|Symbol)\*?\*?\s*:?\s*\*?\*?\s*`?\s*([A-Z0-9]{3,12})\s*`?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SymbolRegex();

    [GeneratedRegex(@"\b([A-Z]{2,6}[0-9]{0,4}|[A-Z]{3}/[A-Z]{3})\b", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolTokenRegex();

    [GeneratedRegex(@"(?:Entry|Цена входа)[^$\d]{0,40}\$?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EntryPriceRegex();

    [GeneratedRegex(@"(?:Price|Цена)\*?\*?\s*:?\s*`?([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstructionPriceRegex();

    [GeneratedRegex(@"(?:Stop\s*Loss|Стоп-лосс|SL)[^$\d]{0,40}\$?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopLossRegex();

    [GeneratedRegex(@"(?:Take\s*Profit|Тейк-профит|TP)[^$\d]{0,40}\$?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TakeProfitRegex();

    [GeneratedRegex(@"(?:Лот|Volume|lot)\*?\*?\s*:?\s*\*?\*?\s*([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LotRegex();

    [GeneratedRegex(@"(?:Тестовый\s+сигнал|Trade\s+signal|Сигнал)\s*:?\s*([^\n\r]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignalHeadingRegex();
}
