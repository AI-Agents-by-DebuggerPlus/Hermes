using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>Packs cards onto screens; words can use 1–3 columns.</summary>
public static class LessonPager
{
    public static IReadOnlyList<LessonScreen> BuildScreens(
        LessonDocument lesson,
        Size viewport,
        AppSettings settings,
        Thickness padding)
    {
        var screens = new List<LessonScreen>();
        var contentWidth = Math.Max(80, viewport.Width - padding.Left - padding.Right);
        var contentHeight = Math.Max(80, viewport.Height - padding.Top - padding.Bottom);
        var en = settings.EnglishFontSize;
        var ru = settings.RussianFontSize;
        const double spacing = 22;

        AddSection(screens, LessonSection.Title, "Название",
            lesson.TitleCards.Count > 0 ? lesson.TitleCards : FallbackTitle(lesson),
            contentWidth, contentHeight, en, ru, spacing, columns: 1);

        AddSection(screens, LessonSection.Words, "Слова",
            lesson.Words, contentWidth, contentHeight, en * 0.88, ru * 0.88, spacing,
            columns: Math.Max(1, Math.Min(3, settings.WordColumns)));

        AddSection(screens, LessonSection.Phrases, "Словосочетания",
            lesson.Phrases, contentWidth, contentHeight, en * 0.95, ru * 0.95, spacing, columns: 1);

        AddSection(screens, LessonSection.Lyrics, "Предложения",
            lesson.Lyrics, contentWidth, contentHeight, en, ru, spacing, columns: 1);

        return screens;
    }

    private static List<CardPair> FallbackTitle(LessonDocument lesson)
    {
        var list = new List<CardPair>();
        if (!string.IsNullOrWhiteSpace(lesson.TitleEn))
        {
            list.Add(new CardPair(lesson.TitleEn, lesson.TitleRu));
        }

        if (!string.IsNullOrWhiteSpace(lesson.Artist))
        {
            list.Add(new CardPair(lesson.Artist, string.Empty));
        }

        return list;
    }

    private static void AddSection(
        List<LessonScreen> screens,
        LessonSection section,
        string label,
        IList<CardPair> cards,
        double width,
        double height,
        double enSize,
        double ruSize,
        double spacing,
        int columns)
    {
        if (cards == null || cards.Count == 0)
        {
            return;
        }

        var pages = columns > 1
            ? PaginateGrid(cards, width, height, enSize, ruSize, spacing, columns)
            : Paginate(cards, width, height, enSize, ruSize, spacing);

        for (var i = 0; i < pages.Count; i++)
        {
            screens.Add(new LessonScreen
            {
                Section = section,
                SectionLabel = label,
                Cards = pages[i],
                ScreenIndex = i + 1,
                ScreenCountInSection = pages.Count,
                ColumnCount = columns,
            });
        }
    }

    private static List<List<CardPair>> Paginate(
        IList<CardPair> cards,
        double width,
        double height,
        double enSize,
        double ruSize,
        double spacing)
    {
        var pages = new List<List<CardPair>>();
        var current = new List<CardPair>();
        var used = 0.0;

        foreach (var card in cards)
        {
            var h = MeasureCardHeight(card, width, enSize, ruSize);
            var need = current.Count == 0 ? h : h + spacing;

            if (current.Count > 0 && used + need > height)
            {
                pages.Add(current);
                current = new List<CardPair>();
                used = 0;
                need = h;
            }

            current.Add(card);
            used += need;
        }

        if (current.Count > 0)
        {
            pages.Add(current);
        }

        return pages;
    }

    /// <summary>
    /// Fill column-major grid: compute how many rows fit, then pack columns×rows per page.
    /// </summary>
    private static List<List<CardPair>> PaginateGrid(
        IList<CardPair> cards,
        double width,
        double height,
        double enSize,
        double ruSize,
        double spacing,
        int columns)
    {
        var colWidth = (width - (columns - 1) * 16) / columns;
        // Use a representative tall card height estimate from actual cards
        var maxCardH = 0.0;
        foreach (var c in cards)
        {
            maxCardH = Math.Max(maxCardH, MeasureCardHeight(c, colWidth, enSize, ruSize));
        }

        if (maxCardH < 1)
        {
            maxCardH = enSize + ruSize + 20;
        }

        var rows = Math.Max(1, (int)Math.Floor((height + spacing) / (maxCardH + spacing)));
        var perPage = Math.Max(columns, rows * columns);

        var pages = new List<List<CardPair>>();
        for (var i = 0; i < cards.Count; i += perPage)
        {
            var page = new List<CardPair>();
            for (var j = i; j < cards.Count && j < i + perPage; j++)
            {
                page.Add(cards[j]);
            }

            pages.Add(page);
        }

        return pages;
    }

    public static double MeasureCardHeight(CardPair card, double width, double enSize, double ruSize)
    {
        var en = MeasureText(card.En ?? string.Empty, width, enSize, FontWeights.Bold);
        var ru = string.IsNullOrWhiteSpace(card.Ru)
            ? 0
            : MeasureText(card.Ru, width, ruSize, FontWeights.Normal) + 8;
        return en + ru + 6;
    }

    private static double MeasureText(string text, double width, double fontSize, FontWeight weight)
    {
        if (string.IsNullOrEmpty(text))
        {
            return fontSize * 1.2;
        }

        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Width = width,
        };
        tb.Measure(new Size(width, double.PositiveInfinity));
        return Math.Max(fontSize * 1.2, tb.DesiredSize.Height);
    }

    public static string FormatProgress(LessonScreen screen, int globalIndex, int globalCount)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} · {1}/{2} · экран {3}/{4}",
            screen.SectionLabel,
            globalIndex + 1,
            globalCount,
            screen.ScreenIndex,
            screen.ScreenCountInSection);
    }
}
