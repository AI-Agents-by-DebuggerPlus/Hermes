using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

public sealed class TtsUtterance
{
    public TtsUtterance(string text, bool english)
    {
        Text = text ?? string.Empty;
        English = english;
    }

    public string Text { get; }
    public bool English { get; }
}

/// <summary>Builds EN→RU→EN→EN sequences; '/' becomes a break between parts (not spoken).</summary>
public static class TtsUtterancePlanner
{
    private static readonly Regex SlashSplit = new(@"\s*/\s*", RegexOptions.Compiled);

    /// <summary>
    /// Optional gender/number endings in parentheses are not spoken:
    /// имел(а) → имел, нашёл(а) → нашёл.
    /// </summary>
    private static readonly Regex OptionalEndingInParens = new(
        @"\(([а-яёa-z]{1,3}(/[а-яёa-z]{1,3})*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string NormalizeForSpeech(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var t = text.Replace('\n', ' ').Trim();
        t = OptionalEndingInParens.Replace(t, string.Empty);
        return Regex.Replace(t, @"\s{2,}", " ").Trim();
    }

    public static IReadOnlyList<TtsUtterance> FromScreen(LessonScreen screen)
    {
        var list = new List<TtsUtterance>();
        if (screen?.Cards == null)
        {
            return list;
        }

        foreach (var card in screen.Cards)
        {
            AppendCard(list, card);
        }

        return list;
    }

    public static IReadOnlyList<TtsUtterance> FromLesson(LessonDocument lesson)
    {
        var list = new List<TtsUtterance>();
        if (lesson == null)
        {
            return list;
        }

        AppendCards(list, lesson.TitleCards);
        AppendCards(list, lesson.Words);
        AppendCards(list, lesson.Phrases);
        AppendCards(list, lesson.Lyrics);
        return list;
    }

    private static void AppendCards(List<TtsUtterance> list, IEnumerable<CardPair> cards)
    {
        foreach (var card in cards)
        {
            AppendCard(list, card);
        }
    }

    private static void AppendCard(List<TtsUtterance> list, CardPair card)
    {
        AppendSpoken(list, card.En, english: true);
        AppendSpoken(list, card.Ru, english: false);
        AppendSpoken(list, card.En, english: true);
        AppendSpoken(list, card.En, english: true);
    }

    private static void AppendSpoken(List<TtsUtterance> list, string text, bool english)
    {
        var normalized = NormalizeForSpeech(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var parts = SlashSplit.Split(normalized);
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            list.Add(new TtsUtterance(part, english));
        }
    }
}
