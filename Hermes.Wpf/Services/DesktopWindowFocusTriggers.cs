using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

public static class DesktopWindowFocusTriggers
{
    private static readonly Regex FocusPattern = new(
        @"(?:переключ(?:ись|и)|перейд(?:и|ите)|активируй|сфокусируй(?:ся)?|открой|focus(?:\s+on)?|switch\s+to)\s+"
        + @"(?:в|на|к|to)?\s*(?:окн[оаеу]?\s+)?(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParseTarget(string? message, out string target)
    {
        target = string.Empty;
        var t = (message ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        var m = FocusPattern.Match(t);
        if (!m.Success)
        {
            return false;
        }

        target = m.Groups["target"].Value.Trim().TrimEnd('.', '!', '?', '"', '\'');
        return target.Length >= 2;
    }

    public static bool Matches(string? message) => TryParseTarget(message, out _);
}
