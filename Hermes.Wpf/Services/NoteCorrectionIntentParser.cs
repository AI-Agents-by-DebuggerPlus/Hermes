using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Parses agent JSON {"skill":"note_correction",...} (design §2.1 explicit path).</summary>
internal static class NoteCorrectionIntentParser
{
    internal sealed record Intent(string What, string Expected, string Actual, string TaskType);

    internal static bool TryConsume(string assistantText, out Intent? intent, out string displayWithoutJson)
    {
        intent = null;
        displayWithoutJson = assistantText ?? string.Empty;
        foreach (var json in EnumerateJsonCandidates(assistantText ?? string.Empty))
        {
            if (!TryParse(json, out var parsed))
            {
                continue;
            }

            intent = parsed;
            displayWithoutJson = Regex.Replace(
                displayWithoutJson.Replace(json, string.Empty, StringComparison.Ordinal),
                @"\n{3,}",
                "\n\n").Trim();
            return true;
        }

        return false;
    }

    private static bool TryParse(string json, out Intent intent)
    {
        intent = null!;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("skill", out var sk)
                || !string.Equals(sk.GetString()?.Trim(), "note_correction", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var what = Read(root, "what");
            var expected = Read(root, "expected");
            if (expected.Length == 0)
            {
                expected = Read(root, "corrected_to");
            }

            var actual = Read(root, "actual");
            if (actual.Length == 0)
            {
                actual = Read(root, "actual_first");
            }

            var taskType = Read(root, "task_type");
            if (what.Length == 0 && expected.Length == 0 && actual.Length == 0)
            {
                return false;
            }

            intent = new Intent(
                what.Length == 0 ? "correction" : what,
                expected,
                actual,
                taskType);
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

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        foreach (Match m in Regex.Matches(text, @"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", RegexOptions.Singleline))
        {
            if (m.Value.Contains("note_correction", StringComparison.OrdinalIgnoreCase))
            {
                yield return m.Value;
            }
        }

        foreach (Match m in Regex.Matches(
                     text,
                     @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
                     RegexOptions.IgnoreCase))
        {
            if (m.Groups[1].Value.Contains("note_correction", StringComparison.OrdinalIgnoreCase))
            {
                yield return m.Groups[1].Value;
            }
        }
    }
}
