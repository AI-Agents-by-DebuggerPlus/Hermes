using System.Text.Json;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Wpf.Services;

internal static class TradingPlatformIntentParser
{
    internal static bool TryConsumeIntent(
        string assistantText,
        out TradingPlatformCommand? command,
        out bool queryOnly,
        out string? market)
    {
        command = null;
        queryOnly = false;
        market = null;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParse(json, out var cmd, out var query, out var parsedMarket))
            {
                continue;
            }

            market = parsedMarket;
            if (query)
            {
                queryOnly = true;
                return true;
            }

            command = cmd;
            return command is not null;
        }

        return false;
    }

    internal static bool TryConsumeIntent(string assistantText, out TradingPlatformCommand? command, out bool queryOnly)
    {
        var found = TryConsumeIntent(assistantText, out command, out queryOnly, out _);
        return found;
    }

    internal static string UserFacingLine(bool ok, string detail)
    {
        var text = TryExtractResultMessage(detail) ?? detail.Trim();
        if (string.IsNullOrEmpty(text))
        {
            text = ok
                ? "команда выполнена"
                : "нет ответа от торгового терминала (запустите Binance Demo Futures Terminal)";
        }

        return ok ? $"[trading] {text}" : $"[trading] Ошибка: {text}";
    }

    internal static string? TryExtractResultMessagePublic(string detail) => TryExtractResultMessage(detail);

    private static string? TryExtractResultMessage(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || detail[0] != '{')
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(detail);
            if (doc.RootElement.TryGetProperty("Message", out var msg))
            {
                return msg.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool TryParse(string json, out TradingPlatformCommand? command, out bool queryOnly, out string? market)
    {
        command = null;
        queryOnly = false;
        market = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk) ||
                !string.Equals(sk.GetString()?.Trim(), "trading", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            market = NullIfEmpty(ReadString(root, "market"));

            var action = ReadString(root, "action");
            if (action.Equals("query", StringComparison.OrdinalIgnoreCase))
            {
                queryOnly = true;
                return true;
            }

            command = new TradingPlatformCommand
            {
                Action = action,
                Symbol = NullIfEmpty(ReadString(root, "symbol")),
                Side = NullIfEmpty(ReadString(root, "side")),
                OrderType = NullIfEmpty(ReadString(root, "order_type")),
                Quantity = ReadDecimal(root, "quantity"),
                QuantityUsdt = ReadDecimal(root, "quantity_usdt") ?? ReadDecimal(root, "quantity"),
                Price = ReadDecimal(root, "price"),
                ReduceOnly = ReadBool(root, "reduce_only"),
                OrderId = NullIfEmpty(ReadString(root, "order_id")),
                StrategyId = NullIfEmpty(ReadString(root, "strategy_id")),
                Enabled = ReadBool(root, "enabled"),
                Leverage = ReadInt(root, "leverage"),
                RequestedBy = "Hermes.Wpf",
            };
            return !string.IsNullOrWhiteSpace(command.Action);
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool TryParse(string json, out TradingPlatformCommand? command, out bool queryOnly)
    {
        var ok = TryParse(json, out command, out queryOnly, out _);
        return ok;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith('{') && t.EndsWith('}'))
            {
                yield return t;
            }
        }

        var start = text.IndexOf('{');
        while (start >= 0)
        {
            var depth = 0;
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        yield return text[start..(i + 1)];
                        start = text.IndexOf('{', i + 1);
                        break;
                    }
                }
            }

            if (depth != 0)
            {
                break;
            }
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(el.GetString(), out var d) => d,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null,
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
