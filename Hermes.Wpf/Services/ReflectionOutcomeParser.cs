using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

internal static class ReflectionOutcomeParser
{
    internal sealed record Outcome(string Action, string Reason, string Name, string Content, string Text);

    internal static bool TryParse(string assistantText, out Outcome outcome)
    {
        outcome = null!;
        foreach (var json in EnumerateCandidates(assistantText ?? string.Empty))
        {
            if (!TryParseOne(json, out outcome))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryParseOne(string json, out Outcome outcome)
    {
        outcome = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("action", out var actEl))
            {
                return false;
            }

            var action = (actEl.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            if (action is not ("none" or "skill_draft" or "memory_fact"))
            {
                return false;
            }

            outcome = new Outcome(
                action,
                Read(root, "reason"),
                Read(root, "name"),
                Read(root, "content"),
                Read(root, "text"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? string.Empty).Trim()
            : string.Empty;

    private static IEnumerable<string> EnumerateCandidates(string text)
    {
        foreach (Match m in Regex.Matches(
                     text,
                     @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
                     RegexOptions.IgnoreCase))
        {
            yield return m.Groups[1].Value;
        }

        foreach (Match m in Regex.Matches(text, @"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", RegexOptions.Singleline))
        {
            if (m.Value.Contains("\"action\"", StringComparison.OrdinalIgnoreCase))
            {
                yield return m.Value;
            }
        }
    }
}
