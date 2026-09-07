using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Mt5Terminal trade router: agent may only emit a whitelist JSON task (or unsupported).
/// </summary>
public static partial class Mt5TerminalTradeRouter
{
    public const string UnsupportedAction = "unsupported";

    public static readonly HashSet<string> Whitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        "snapshot",
        "set_lot",
        "set_real_trading",
        "set_auto_trade",
        "buy_market",
        "sell_market",
        "close_all",
        "close_slot",
        "screenshot",
        "refresh",
        "place_pending",
        "list_symbols",
        UnsupportedAction
    };

    public static bool IsMt5TerminalProject(string? projectName) =>
        string.Equals((projectName ?? string.Empty).Trim(), "Mt5Terminal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chart screenshot request (HWT/MT5), not desktop/browser capture.
    /// </summary>
    public static bool LooksLikeChartScreenshotRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return false;
        }

        // Exact / short intents
        if (t is "скриншот" or "screenshot" or "снимок" or "снимок графика" or "chart screenshot"
            or "screeshot" or "screnshot" or "screenshoot")
        {
            return true;
        }

        if (t.Contains("скриншот", StringComparison.Ordinal)
            || t.Contains("screenshot", StringComparison.Ordinal)
            || t.Contains("снимок графика", StringComparison.Ordinal)
            || t.Contains("screen shot", StringComparison.Ordinal)
            || LooksLikeScreenshotTypo(t))
        {
            // Avoid remapping status questions that only mention the word in passing.
            if (t.Contains("позиц", StringComparison.Ordinal)
                || t.Contains("статус", StringComparison.Ordinal)
                || t.Contains("баланс", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>Common EN typos: screeshot, screnShot, screenshoot, …</summary>
    private static bool LooksLikeScreenshotTypo(string t)
    {
        // Strip spaces/punctuation for short one-word intents from Android/STT.
        var compact = ScreenshotTypoCompactRegex().Replace(t, string.Empty);
        if (compact.Length is < 8 or > 16)
        {
            return false;
        }

        // scr…shot / skr…shot (missing/extra letters between)
        return ScreenshotTypoTokenRegex().IsMatch(compact);
    }

    /// <summary>Push HWT status to RemoteTerminal.</summary>
    public static bool LooksLikeRefreshRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return false;
        }

        if (t is "refresh" or "обновить" or "обнови" or "refresh terminal" or "обновить терминал"
            or "обнови remoteterminal" or "обновить remoteterminal")
        {
            return true;
        }

        return t.Equals("refresh", StringComparison.OrdinalIgnoreCase)
               || (t.Contains("refresh", StringComparison.Ordinal)
                   && !t.Contains("скрин", StringComparison.Ordinal))
               || (t.Contains("обнов", StringComparison.Ordinal)
                   && t.Contains("remoteterminal", StringComparison.Ordinal));
    }

    /// <summary>Status / balance / positions text request → snapshot (not screenshot).</summary>
    public static bool LooksLikeStatusOrBalanceRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0 || LooksLikeChartScreenshotRequest(t))
        {
            return false;
        }

        if (t is "статус" or "баланс" or "позиции" or "status" or "balance" or "snapshot")
        {
            return true;
        }

        return t.Contains("баланс", StringComparison.Ordinal)
               || t.Contains("счёт", StringComparison.Ordinal)
               || t.Contains("счет", StringComparison.Ordinal)
               || t.Contains("позици", StringComparison.Ordinal)
               || LooksLikePriceOnlyRequest(t)
               || (t.Contains("статус", StringComparison.Ordinal)
                   && (t.Contains("терминал", StringComparison.Ordinal) || t.Contains("hwt", StringComparison.Ordinal)));
    }

    /// <summary>Ask for active chart quote only (bid/ask), not full status.</summary>
    public static bool LooksLikePriceOnlyRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0 || LooksLikeChartScreenshotRequest(t) || LooksLikeBalanceOnlyRequest(t))
        {
            return false;
        }

        if (t is "цена" or "цена?" or "price" or "price?" or "котировка" or "bid" or "ask"
            or "цена графика" or "цена графика?" or "текущая цена" or "текущая цена?"
            or "цена активного инструмента" or "цена активного инструмента?")
        {
            return true;
        }

        // Long form: «Текущая цена инструмента активного графика?»
        if (t.Contains("цена", StringComparison.Ordinal)
            && (t.Contains("график", StringComparison.Ordinal)
                || t.Contains("инструмент", StringComparison.Ordinal)
                || t.Contains("котир", StringComparison.Ordinal)
                || t.Contains("bid", StringComparison.Ordinal)
                || t.Contains("ask", StringComparison.Ordinal)))
        {
            return true;
        }

        return t.Contains("текущая цена", StringComparison.Ordinal)
               || t.Contains("текущую цену", StringComparison.Ordinal);
    }

    /// <summary>Ask for account balance only (not full terminal status / positions dump).</summary>
    public static bool LooksLikeBalanceOnlyRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0 || LooksLikeChartScreenshotRequest(t))
        {
            return false;
        }

        if (t.Contains("позици", StringComparison.Ordinal)
            || t.Contains("статус", StringComparison.Ordinal)
            || t.Contains("snapshot", StringComparison.Ordinal))
        {
            return false;
        }

        if (t is "баланс" or "balance")
        {
            return true;
        }

        return t.Contains("баланс", StringComparison.Ordinal)
               || t.Contains("balance", StringComparison.Ordinal)
               || ((t.Contains("счёт", StringComparison.Ordinal) || t.Contains("счет", StringComparison.Ordinal))
                   && (t.Contains("какой", StringComparison.Ordinal)
                       || t.Contains("сколько", StringComparison.Ordinal)
                       || t.Contains("покажи", StringComparison.Ordinal)
                       || t.Contains("текущ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Replay last successful chart screenshot to RemoteTerminal (no new ChartScreenShot / no Hermes CLI).
    /// </summary>
    public static bool LooksLikeRepeatRequest(string? userMessage)
    {
        var t = (userMessage ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0)
        {
            return false;
        }

        if (t is "repeat" or "повтор" or "повтори" or "ещё раз" or "еще раз" or "again"
            or "repeat screenshot" or "повтор скриншота")
        {
            return true;
        }

        return t.StartsWith("повтор", StringComparison.Ordinal)
               || t.Equals("repeat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Agents often confuse «скриншот» with <c>snapshot</c> (status JSON). Fix clear chart intents.
    /// </summary>
    public static Mt5TerminalRouteCommand CorrectRouteForUserIntent(Mt5TerminalRouteCommand route, string? userMessage)
    {
        if (LooksLikeRefreshRequest(userMessage)
            && !string.Equals(route.Action, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            return new Mt5TerminalRouteCommand
            {
                Action = "refresh",
                Id = string.IsNullOrWhiteSpace(route.Id) ? Guid.NewGuid().ToString("N") : route.Id,
            };
        }

        if (!LooksLikeChartScreenshotRequest(userMessage))
        {
            return route;
        }

        if (string.Equals(route.Action, "screenshot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Action, "chart_screenshot", StringComparison.OrdinalIgnoreCase))
        {
            return route;
        }

        // Common mis-route: snapshot / unsupported / wrong action
        if (string.Equals(route.Action, "snapshot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Action, UnsupportedAction, StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Action, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(route.Action, "get_status", StringComparison.OrdinalIgnoreCase))
        {
            return new Mt5TerminalRouteCommand
            {
                Action = "screenshot",
                Id = string.IsNullOrWhiteSpace(route.Id) ? Guid.NewGuid().ToString("N") : route.Id,
            };
        }

        return route;
    }

    /// <summary>Extract the last whitelist/unsupported trade-router JSON object from CLI stdout.</summary>
    public static Mt5TerminalRouteCommand? TryParseFromAgentOutput(string? combinedOrDisplayText)
    {
        var text = combinedOrDisplayText ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        // Prefer fenced ```json ... ``` then raw objects containing "action".
        foreach (Match m in JsonFenceRegex().Matches(text).Cast<Match>().Reverse())
        {
            var parsed = TryParseObject(m.Groups[1].Value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        foreach (var candidate in EnumerateJsonObjects(text).Reverse())
        {
            var parsed = TryParseObject(candidate);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    public static Mt5TerminalRouteCommand? TryParseObject(string json)
    {
        json = (json ?? string.Empty).Trim();
        if (json.Length < 2 || json[0] != '{')
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // {"hermes_wpf_terminal":{...}} envelope
            if (root.TryGetProperty("hermes_wpf_terminal", out var nested)
                && nested.ValueKind == JsonValueKind.Object)
            {
                root = nested;
            }

            if (!root.TryGetProperty("action", out var actionEl)
                || actionEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var action = (actionEl.GetString() ?? string.Empty).Trim();
            if (action.Length == 0 || !Whitelist.Contains(action))
            {
                return null;
            }

            var cmd = new Mt5TerminalRouteCommand
            {
                Action = action.ToLowerInvariant(),
                Id = ReadString(root, "id") ?? Guid.NewGuid().ToString("N"),
                Reason = ReadString(root, "reason"),
            };

            if (root.TryGetProperty("slot", out var slotEl) && slotEl.TryGetInt32(out var slot))
            {
                cmd.Slot = slot;
            }

            if (root.TryGetProperty("lot", out var lotEl) && lotEl.TryGetDouble(out var lot))
            {
                cmd.Lot = lot;
            }

            if (root.TryGetProperty("value", out var valueEl))
            {
                if (valueEl.ValueKind == JsonValueKind.True)
                {
                    cmd.Value = true;
                }
                else if (valueEl.ValueKind == JsonValueKind.False)
                {
                    cmd.Value = false;
                }
            }

            cmd.Symbol = ReadString(root, "symbol");
            cmd.PendingOrderType = ReadString(root, "order_type_label") ?? ReadString(root, "pending_type");
            if (root.TryGetProperty("price", out var priceEl) && priceEl.TryGetDouble(out var price))
            {
                cmd.Price = price;
            }

            if (root.TryGetProperty("stop_loss", out var slEl) && slEl.TryGetDouble(out var sl))
            {
                cmd.StopLoss = sl;
            }

            if (root.TryGetProperty("take_profit", out var tpEl) && tpEl.TryGetDouble(out var tp))
            {
                cmd.TakeProfit = tp;
            }

            return cmd;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string FormatUnsupportedChat(Mt5TerminalRouteCommand cmd)
    {
        var reason = string.IsNullOrWhiteSpace(cmd.Reason)
            ? "Запрос не понят или подходящей задачи нет в белом списке."
            : cmd.Reason.Trim();
        return "Задача не выполнена (unsupported).\n" + reason;
    }

    public static string FormatMissingJsonChat()
    {
        return
            "Роутер не вернул JSON задачи из белого списка.\n"
            + "Ожидается один объект, например "
            + "{\"action\":\"close_all\",\"id\":\"…\"} "
            + "или {\"action\":\"unsupported\",\"reason\":\"…\"}.\n"
            + "Исполнение не запускалось.";
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static IEnumerable<string> EnumerateJsonObjects(string text)
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
                        yield return text.Substring(i, j - i + 1);
                        break;
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFenceRegex();

    [GeneratedRegex(@"[^a-zа-яё0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenshotTypoCompactRegex();

    // screeshot / screenshot / skrinshot / scrnshot …
    [GeneratedRegex(@"^(?:scr+|skr+)[een]*s*h*o*t$", RegexOptions.CultureInvariant)]
    private static partial Regex ScreenshotTypoTokenRegex();
}

public sealed class Mt5TerminalRouteCommand
{
    public required string Action { get; init; }
    public required string Id { get; set; }
    public string? Reason { get; init; }
    public int? Slot { get; set; }
    public double? Lot { get; set; }
    public bool? Value { get; set; }
    public string? Symbol { get; set; }
    public string? PendingOrderType { get; set; }
    public double? Price { get; set; }
    public double? StopLoss { get; set; }
    public double? TakeProfit { get; set; }

    public bool IsUnsupported =>
        string.Equals(Action, Mt5TerminalTradeRouter.UnsupportedAction, StringComparison.OrdinalIgnoreCase);

    public string ToCommandJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Id);
            writer.WriteString("action", Action);
            if (Slot.HasValue)
            {
                writer.WriteNumber("slot", Slot.Value);
            }

            if (Lot.HasValue)
            {
                writer.WriteNumber("lot", Lot.Value);
            }

            if (Value.HasValue)
            {
                writer.WriteBoolean("value", Value.Value);
            }

            if (!string.IsNullOrWhiteSpace(Symbol))
            {
                writer.WriteString("symbol", Symbol);
            }

            if (!string.IsNullOrWhiteSpace(PendingOrderType))
            {
                writer.WriteString("order_type_label", PendingOrderType);
            }

            if (Price.HasValue)
            {
                writer.WriteNumber("price", Price.Value);
            }

            if (StopLoss.HasValue)
            {
                writer.WriteNumber("stop_loss", StopLoss.Value);
            }

            if (TakeProfit.HasValue)
            {
                writer.WriteNumber("take_profit", TakeProfit.Value);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
