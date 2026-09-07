using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Hermes.EnglishLearning.Xp;

/// <summary>
/// Builds Android TTS content: one JSON object per card line (ordered en/ru).
/// See Docs/SupaBase/Формат_TTS_Android_Assistant.md
/// </summary>
internal static class PageTtsFormatter
{
    /// <summary>
    /// Lesson/start metadata for AndroidChat (not spoken as TTS — has <c>type</c>).
    /// </summary>
    public static string FormatLessonMeta(int totalCards, int totalScreens, string title)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"english_lesson_meta\"");
        sb.Append(",\"total_cards\":").Append(totalCards);
        sb.Append(",\"total_screens\":").Append(totalScreens);
        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append(",\"title\":");
            sb.Append(JsonConvert.SerializeObject(title.Trim()));
        }

        sb.Append('}');
        return sb.ToString();
    }

    public static string FormatScreen(LessonScreen screen, bool isLastScreen)
    {
        if (screen == null || screen.Cards == null || screen.Cards.Count == 0)
            return string.Empty;

        var lines = new List<string>();
        for (var i = 0; i < screen.Cards.Count; i++)
        {
            var isLastCard = isLastScreen && i == screen.Cards.Count - 1;
            var line = FormatCard(screen.Cards[i], isLastCard);
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return string.Join("\n", lines.ToArray());
    }

    public static string FormatCard(CardPair card, bool isLast = false)
    {
        if (card == null) return string.Empty;
        var en = (card.En ?? string.Empty).Trim();
        var ru = (card.Ru ?? string.Empty).Trim();
        if (en.Length == 0 && ru.Length == 0) return string.Empty;

        // Manual JSON so key order is en then ru (Android speaks in key order).
        // Extra keys (last) are ignored by TTS parsers that only read en/ru.
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        if (en.Length > 0)
        {
            sb.Append("\"en\":");
            sb.Append(JsonConvert.SerializeObject(en));
            first = false;
        }

        if (ru.Length > 0)
        {
            if (!first) sb.Append(',');
            sb.Append("\"ru\":");
            sb.Append(JsonConvert.SerializeObject(ru));
            first = false;
        }

        if (isLast)
        {
            if (!first) sb.Append(',');
            sb.Append("\"last\":true");
        }

        sb.Append('}');
        return sb.ToString();
    }
}
