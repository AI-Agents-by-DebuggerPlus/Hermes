using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Detects Trading Analytics agent request to refresh MT5 symbol catalog.</summary>
public static partial class TradingAnalyticsMt5SymbolsParser
{
    public const string SkillName = "mt5_symbols";

    public static bool IsTradingAnalyticsProject(string? projectName) =>
        TradingAnalyticsSignalParser.IsTradingAnalyticsProject(projectName);

    public static bool TryParseFetchRequest(string? text, out bool forceRefresh)
    {
        forceRefresh = false;
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return false;
        }

        foreach (var json in EnumerateCandidateObjects(raw).Reverse())
        {
            if (TryParseObject(json, out forceRefresh))
            {
                return true;
            }
        }

        foreach (Match m in JsonFenceRegex().Matches(raw).Cast<Match>().Reverse())
        {
            if (TryParseObject(m.Groups[1].Value, out forceRefresh))
            {
                return true;
            }
        }

        return false;
    }

    public static string StripFromDisplay(string? text)
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

        while (result.Contains("\n\n\n", StringComparison.Ordinal))
        {
            result = result.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return result.Trim();
    }

    private static bool TryParseObject(string json, out bool forceRefresh)
    {
        forceRefresh = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var skill)
                || skill.ValueKind != JsonValueKind.String
                || !string.Equals(skill.GetString(), SkillName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (root.TryGetProperty("action", out var action)
                && action.ValueKind == JsonValueKind.String)
            {
                var a = action.GetString() ?? string.Empty;
                forceRefresh = string.Equals(a, "refresh", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(a, "fetch", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                forceRefresh = true;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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
                        if (block.Contains("mt5_symbols", StringComparison.OrdinalIgnoreCase))
                        {
                            yield return block;
                        }

                        break;
                    }
                }
            }
        }
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonFenceRegex();
}
