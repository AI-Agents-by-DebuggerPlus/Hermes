using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

internal static class WpfLocalIntentParser
{
    private static readonly Regex IdRegex = new(@"^[a-z][a-z0-9_]{2,47}$", RegexOptions.CultureInvariant);

    internal static bool TryConsumeIntent(string assistantText, out WpfLocalIntent? intent)
    {
        intent = null;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParse(json, out var parsed))
            {
                continue;
            }

            intent = parsed;
            return true;
        }

        return false;
    }

    internal static string FormatDisplayResponse(string cliResponse, WpfLocalActionResult result)
    {
        var comment = StripIntentJson(cliResponse ?? string.Empty).Trim();
        var line = result.UserMessage.Trim();
        if (string.IsNullOrEmpty(comment))
        {
            return line;
        }

        if (string.IsNullOrEmpty(line))
        {
            return comment;
        }

        return $"{comment}\n\n{line}";
    }

    internal static string StripIntentJson(string text)
    {
        var working = text;
        foreach (var json in EnumerateJsonCandidates(text))
        {
            if (TryParse(json, out _))
            {
                working = working.Replace(json, string.Empty, StringComparison.Ordinal);
            }
        }

        return Regex.Replace(working, @"\n{3,}", "\n\n").Trim();
    }

    private static bool TryParse(string json, out WpfLocalIntent intent)
    {
        intent = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk)
                || !string.Equals(sk.GetString()?.Trim(), "wpf_local", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var action = ReadString(root, "action");
            if (action.Length == 0)
            {
                return false;
            }

            intent = new WpfLocalIntent
            {
                Action = action.ToLowerInvariant(),
                UserContext = ReadString(root, "user_context"),
                ScheduleAction = ReadString(root, "schedule_action").ToLowerInvariant(),
                RunAtLocal = ReadDateTime(root, "run_at"),
                WindowStartDay = ReadInt(root, "window_start"),
                WindowEndDay = ReadInt(root, "window_end"),
                Hour = ReadInt(root, "hour"),
                Minute = ReadInt(root, "minute"),
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty).Trim() : string.Empty;

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

    private static DateTime? ReadDateTime(JsonElement root, string name)
    {
        var s = ReadString(root, name);
        if (s.Length == 0)
        {
            return null;
        }

        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length > 0 && trimmed.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase))
        {
            yield return trimmed;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
            {
                continue;
            }

            var depth = 0;
            for (var j = i; j < text.Length; j++)
            {
                if (text[j] == '{')
                {
                    depth++;
                }
                else if (text[j] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var slice = text[i..(j + 1)];
                        if (slice.Contains("\"skill\"", StringComparison.OrdinalIgnoreCase)
                            && slice.Contains("\"wpf_local\"", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return slice;
                        }

                        break;
                    }
                }
            }
        }
    }
}
