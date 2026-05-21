using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

internal static class SkillRunTriggers
{
    private static readonly Regex RunByIdRegex = new(
        @"(?:запуст\w*|run|execute|выполни)\s+(?:навык\s+|skill\s+)?[""']?(?<id>[a-z][a-z0-9_]{2,47})[""']?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool TryParseRunRequest(string? userMessage, out string skillId)
    {
        skillId = string.Empty;
        var text = (userMessage ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var m = RunByIdRegex.Match(text);
        if (!m.Success)
        {
            return false;
        }

        skillId = m.Groups["id"].Value.Trim().ToLowerInvariant();
        return skillId.Length >= 3;
    }
}
