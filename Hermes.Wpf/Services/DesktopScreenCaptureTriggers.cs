using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

public static class DesktopScreenCaptureTriggers
{
    private static readonly string[] Phrases =
    [
        "скриншот монитора",
        "скриншот экрана",
        "снимок монитора",
        "снимок экрана",
        "сделай скриншот",
        "сделай снимок",
        "сними экран",
        "сними скриншот",
        "покажи экран",
        "screenshot monitor",
        "screenshot screen",
        "capture screen",
        "screen capture",
        "screen shot",
        "take a screenshot",
        "take screenshot",
    ];

    private static readonly string[] Words =
    [
        "скриншот",
        "screenshot",
        "snapshot",
        "screencap",
        "скрин",
    ];

    private static readonly Regex WordBoundary = new(
        @"\b(screenshot|snapshot|screencap|screen\s*shot)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool Matches(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var t = message.Trim().ToLowerInvariant();

        foreach (var phrase in Phrases)
        {
            if (t.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var word in Words)
        {
            if (t == word || t.StartsWith(word + " ", StringComparison.Ordinal)
                || t.EndsWith(" " + word, StringComparison.Ordinal)
                || t.Contains(" " + word + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (WordBoundary.IsMatch(message))
        {
            return true;
        }

        return false;
    }
}
