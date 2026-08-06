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
        UnsupportedAction
    };

    public static bool IsMt5TerminalProject(string? projectName) =>
        string.Equals((projectName ?? string.Empty).Trim(), "Mt5Terminal", StringComparison.OrdinalIgnoreCase);

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
}

public sealed class Mt5TerminalRouteCommand
{
    public required string Action { get; init; }
    public required string Id { get; init; }
    public string? Reason { get; init; }
    public int? Slot { get; set; }
    public double? Lot { get; set; }
    public bool? Value { get; set; }

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

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
