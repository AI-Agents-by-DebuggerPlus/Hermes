using System.Collections.Generic;

namespace Hermes.EnglishLearning.Models;

public sealed class LessonDocument
{
    public string TitleEn { get; set; } = string.Empty;
    public string TitleRu { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public List<CardPair> TitleCards { get; } = new();
    public List<CardPair> Words { get; } = new();
    public List<CardPair> Lyrics { get; } = new();
}

public sealed class CardPair
{
    public CardPair()
    {
    }

    public CardPair(string en, string ru)
    {
        En = en ?? string.Empty;
        Ru = ru ?? string.Empty;
    }

    public string En { get; set; } = string.Empty;
    public string Ru { get; set; } = string.Empty;
}

public enum LessonSection
{
    Title,
    Words,
    Lyrics,
}

public sealed class LessonScreen
{
    public LessonSection Section { get; set; }
    public string SectionLabel { get; set; } = string.Empty;
    public IReadOnlyList<CardPair> Cards { get; set; } = new List<CardPair>();
    public int ScreenIndex { get; set; }
    public int ScreenCountInSection { get; set; }
}
