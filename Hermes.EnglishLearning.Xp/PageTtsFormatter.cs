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
    public static string FormatScreen(LessonScreen screen)
    {
        if (screen == null || screen.Cards == null || screen.Cards.Count == 0)
            return string.Empty;

        var lines = new List<string>();
        foreach (var card in screen.Cards)
        {
            var line = FormatCard(card);
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return string.Join("\n", lines.ToArray());
    }

    public static string FormatCard(CardPair card)
    {
        if (card == null) return string.Empty;
        var en = (card.En ?? string.Empty).Trim();
        var ru = (card.Ru ?? string.Empty).Trim();
        if (en.Length == 0 && ru.Length == 0) return string.Empty;

        // Manual JSON so key order is en then ru (Android speaks in key order).
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
        }

        sb.Append('}');
        return sb.ToString();
    }
}
