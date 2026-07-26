using System.Collections.Generic;

namespace Hermes.EnglishLearning.Xp;

internal sealed class LessonDocument
{
    public string TitleEn { get; set; } = string.Empty;
    public string TitleRu { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public List<CardPair> TitleCards { get; } = new List<CardPair>();
    public List<CardPair> Words { get; } = new List<CardPair>();
    public List<CardPair> Phrases { get; } = new List<CardPair>();
    public List<CardPair> Lyrics { get; } = new List<CardPair>();
}

internal sealed class CardPair
{
    public CardPair() { }

    public CardPair(string en, string ru)
    {
        En = en ?? string.Empty;
        Ru = ru ?? string.Empty;
    }

    public string En { get; set; } = string.Empty;
    public string Ru { get; set; } = string.Empty;
}

internal enum LessonSection
{
    Title,
    Words,
    Phrases,
    Lyrics,
}

internal sealed class LessonScreen
{
    public LessonSection Section { get; set; }
    public string SectionLabel { get; set; } = string.Empty;
    public List<CardPair> Cards { get; set; } = new List<CardPair>();
    public int ColumnCount { get; set; } = 1;
}

/// <summary>Remote nav from AndroidChat via Supabase.</summary>
internal enum NavCommand
{
    None,
    FullScreen,
    Next,
    Previous,
    Exit,
}
