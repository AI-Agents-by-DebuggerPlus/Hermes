using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

internal static class SkillCrystallizeTriggers
{
    private static readonly string[] Phrases =
    [
        "сохрани как навык",
        "сохранить как навык",
        "закристаллизуй",
        "кристаллизуй навык",
        "кристаллизация навыка",
        "skill_save",
        "save as skill",
        "crystallize skill",
        "сохрани это как skill",
        "оформи как навык",
    ];

    internal static bool Matches(string? userMessage)
    {
        var text = (userMessage ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var phrase in Phrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return CrystallizeVerbRegex.IsMatch(text);
    }

    private static readonly Regex CrystallizeVerbRegex = new(
        @"\b(сохран\w*|кристал\w*|crystall\w*)\b.{0,40}\b(навык\w*|skill)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
