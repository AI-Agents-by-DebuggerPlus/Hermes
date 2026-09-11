using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Heuristic: user correction in the first turns after an agent reply (design §2.1).</summary>
public static class ImplicitCorrectionDetector
{
    private static readonly Regex StartPatterns = new(
        @"^\s*(нет[,!.\s]|не\s+то|неверно|неправильно|ошибка|я\s+имел\s+в\s+виду|имел\s+в\s+виду|"
        + @"это\s+не\s+|не\s+TD|не\s+так|перепроверь|откуда\s+ты\s+взял|"
        + @"no[,!.\s]|wrong|incorrect|i\s+meant|that'?s\s+not|not\s+TD)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsNotPattern = new(
        @"\bэто\s+не\s+(.+?),\s*это\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryDetect(string userText, out string what, out string correctedTo, out string actualHint)
    {
        what = "user_correction";
        correctedTo = string.Empty;
        actualHint = string.Empty;
        var text = (userText ?? string.Empty).Trim();
        if (text.Length < 3)
        {
            return false;
        }

        if (!StartPatterns.IsMatch(text) && !IsNotPattern.IsMatch(text))
        {
            return false;
        }

        var m = IsNotPattern.Match(text);
        if (m.Success)
        {
            actualHint = m.Groups[1].Value.Trim();
            correctedTo = m.Groups[2].Value.Trim();
            what = "value";
            return true;
        }

        correctedTo = text.Length > 240 ? text[..240] : text;
        return true;
    }
}
