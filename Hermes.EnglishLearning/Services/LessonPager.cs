using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>Packs card pairs onto screens based on available viewport size.</summary>
public static class LessonPager
{
    public static IReadOnlyList<LessonScreen> BuildScreens(
        LessonDocument lesson,
        Size viewport,
        double englishFontSize,
        double russianFontSize,
        double cardSpacing,
        Thickness padding)
    {
        var screens = new List<LessonScreen>();
        var contentWidth = Math.Max(80, viewport.Width - padding.Left - padding.Right);
        var contentHeight = Math.Max(80, viewport.Height - padding.Top - padding.Bottom);

        AddSection(screens, LessonSection.Title, "Название",
            lesson.TitleCards.Count > 0 ? lesson.TitleCards : FallbackTitle(lesson),
            contentWidth, contentHeight, englishFontSize, russianFontSize, cardSpacing);

        AddSection(screens, LessonSection.Words, "Слова",
            lesson.Words, contentWidth, contentHeight, englishFontSize * 0.92, russianFontSize * 0.92, cardSpacing);

        AddSection(screens, LessonSection.Lyrics, "Текст",
            lesson.Lyrics, contentWidth, contentHeight, englishFontSize, russianFontSize, cardSpacing);

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
        double spacing)
    {
        if (cards == null || cards.Count == 0)
        {
            return;
        }

        var pages = Paginate(cards, width, height, enSize, ruSize, spacing);
        for (var i = 0; i < pages.Count; i++)
        {
            screens.Add(new LessonScreen
            {
                Section = section,
                SectionLabel = label,
                Cards = pages[i],
                ScreenIndex = i + 1,
                ScreenCountInSection = pages.Count,
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

            // Always place at least one card even if taller than viewport
            current.Add(card);
            used += need;
        }

        if (current.Count > 0)
        {
            pages.Add(current);
        }

        return pages;
    }

    public static double MeasureCardHeight(CardPair card, double width, double enSize, double ruSize)
    {
        var en = MeasureText(card.En ?? string.Empty, width, enSize, FontWeights.Bold);
        var ru = string.IsNullOrWhiteSpace(card.Ru)
            ? 0
            : MeasureText(card.Ru, width, ruSize, FontWeights.Normal) + 10;
        return en + ru + 8;
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
