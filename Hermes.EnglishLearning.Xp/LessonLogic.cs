using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Hermes.EnglishLearning.Xp;

/// <summary>
/// Parses lesson MD and AndroidChat remote-nav payloads for recipient EnglishLearning.
/// </summary>
internal static class MessageParser
{
    public static bool TryExtractLesson(string content, out string markdown, out string title)
    {
        markdown = string.Empty;
        title = null;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var t = content.Trim();
        if (t.StartsWith("[LOG:", StringComparison.Ordinal)) return false;
        if (TryNav(t, out _)) return false;

        if (t.StartsWith("---", StringComparison.Ordinal)
            || t.StartsWith("## title", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("# ", StringComparison.Ordinal))
        {
            if (t.IndexOf("## words", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("## lyrics", StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("## title", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                markdown = t;
                return true;
            }
        }

        if (!t.StartsWith("{", StringComparison.Ordinal)) return false;

        try
        {
            var obj = JObject.Parse(t);
            var type = obj["type"]?.ToString() ?? string.Empty;
            if (string.Equals(type, "english_nav", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "english_learning_nav", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(type, "english_lesson", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "english_cards", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            title = obj["title"]?.ToString();
            markdown = obj["markdown"]?.ToString()
                       ?? obj["md"]?.ToString()
                       ?? obj["content"]?.ToString()
                       ?? string.Empty;
            return !string.IsNullOrWhiteSpace(markdown);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryNav(string content, out NavCommand command)
    {
        command = NavCommand.None;
        if (string.IsNullOrWhiteSpace(content)) return false;
        var t = content.Trim();

        // Plain / Tasker-friendly
        if (t.StartsWith("[NAV:", StringComparison.OrdinalIgnoreCase) && t.EndsWith("]"))
        {
            var inner = t.Substring(5, t.Length - 6).Trim();
            command = MapCommand(inner);
            return command != NavCommand.None;
        }

        if (t.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var obj = JObject.Parse(t);
                var type = obj["type"]?.ToString() ?? string.Empty;
                if (!string.Equals(type, "english_nav", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "english_learning_nav", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "nav", StringComparison.OrdinalIgnoreCase))
                {
                    // Also accept {"command":"next"} without type when clearly a nav verb.
                    var onlyCmd = obj["command"]?.ToString() ?? obj["action"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(onlyCmd) && obj["markdown"] == null && obj["type"] == null)
                    {
                        command = MapCommand(onlyCmd);
                        return command != NavCommand.None;
                    }

                    return false;
                }

                var cmd = obj["command"]?.ToString()
                          ?? obj["action"]?.ToString()
                          ?? obj["nav"]?.ToString()
                          ?? string.Empty;
                command = MapCommand(cmd);
                return command != NavCommand.None;
            }
            catch
            {
                return false;
            }
        }

        // Bare verbs (AndroidChat one-liners)
        command = MapCommand(t);
        return command != NavCommand.None
               && (string.Equals(t, "fullscreen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "full_screen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "full screen", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "next", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "previous", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "prev", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "exit", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(t, "close", StringComparison.OrdinalIgnoreCase));
    }

    private static NavCommand MapCommand(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return NavCommand.None;
        var c = raw.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        switch (c)
        {
            case "fullscreen":
            case "fullscr":
            case "fs":
            case "togglefullscreen":
                return NavCommand.FullScreen;
            case "next":
            case "nextscreen":
            case "forward":
            case "right":
                return NavCommand.Next;
            case "previous":
            case "prev":
            case "prevscreen":
            case "back":
            case "left":
                return NavCommand.Previous;
            case "exit":
            case "close":
            case "quit":
                return NavCommand.Exit;
            default:
                return NavCommand.None;
        }
    }
}

internal static class LessonMarkdownParser
{
    public static LessonDocument Parse(string markdown)
    {
        var doc = new LessonDocument();
        if (string.IsNullOrWhiteSpace(markdown)) return doc;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                ParseMeta(doc, lines[i]);
                i++;
            }

            if (i < lines.Length && lines[i].Trim() == "---") i++;
        }

        string section = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (section == null) return;
            var cards = ParseCards(section, buffer);
            if (section == "title")
            {
                doc.TitleCards.AddRange(cards);
                if (string.IsNullOrWhiteSpace(doc.TitleEn) && cards.Count > 0)
                {
                    doc.TitleEn = cards[0].En;
                    doc.TitleRu = cards[0].Ru;
                }
            }
            else if (section == "words") doc.Words.AddRange(cards);
            else if (section == "phrases") doc.Phrases.AddRange(cards);
            else if (section == "lyrics") doc.Lyrics.AddRange(cards);
            buffer.Clear();
        }

        for (; i < lines.Length; i++)
        {
            var trim = lines[i].Trim();
            if (trim.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                section = NormalizeSection(trim.Substring(2).Trim());
                continue;
            }

            if (section != null) buffer.Add(lines[i]);
        }

        Flush();
        if (doc.TitleCards.Count == 0 && !string.IsNullOrWhiteSpace(doc.TitleEn))
        {
            doc.TitleCards.Add(new CardPair(doc.TitleEn, doc.TitleRu));
            if (!string.IsNullOrWhiteSpace(doc.Artist))
                doc.TitleCards.Add(new CardPair(doc.Artist, string.Empty));
        }

        return doc;
    }

    private static void ParseMeta(LessonDocument doc, string line)
    {
        var t = line.Trim();
        var idx = t.IndexOf(':');
        if (idx <= 0) return;
        var key = t.Substring(0, idx).Trim().ToLowerInvariant();
        var val = t.Substring(idx + 1).Trim().Trim('"');
        if (key == "title" || key == "title_en") doc.TitleEn = val;
        else if (key == "title_ru") doc.TitleRu = val;
        else if (key == "artist") doc.Artist = val;
    }

    private static string NormalizeSection(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.StartsWith("title")) return "title";
        if (n.StartsWith("word") || n.StartsWith("vocab")) return "words";
        if (n.StartsWith("phrase") || n.StartsWith("example")) return "phrases";
        if (n.StartsWith("lyric") || n.StartsWith("sentence") || n.StartsWith("line")) return "lyrics";
        return n;
    }

    private static List<CardPair> ParseCards(string section, List<string> lines)
    {
        if (section == "lyrics" || section == "phrases")
            return ParseBlocks(lines);
        return ParsePipeLines(lines);
    }

    private static List<CardPair> ParsePipeLines(IEnumerable<string> lines)
    {
        var result = new List<CardPair>();
        string pendingEn = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line == "---") continue;
            var pipe = line.IndexOf('|');
            if (pipe >= 0)
            {
                result.Add(new CardPair(line.Substring(0, pipe).Trim(), line.Substring(pipe + 1).Trim()));
                pendingEn = null;
                continue;
            }

            if (pendingEn == null) pendingEn = line;
            else
            {
                result.Add(new CardPair(pendingEn, line));
                pendingEn = null;
            }
        }

        if (pendingEn != null) result.Add(new CardPair(pendingEn, string.Empty));
        return result;
    }

    private static List<CardPair> ParseBlocks(List<string> lines)
    {
        var result = new List<CardPair>();
        var block = new List<string>();
        foreach (var raw in lines)
        {
            if (raw.Trim() == "---")
            {
                result.AddRange(ParsePipeLines(block));
                block.Clear();
            }
            else block.Add(raw);
        }

        result.AddRange(ParsePipeLines(block));
        return result;
    }
}

internal static class LessonPager
{
    public static List<LessonScreen> Build(LessonDocument lesson, AppSettings settings)
    {
        var screens = new List<LessonScreen>();
        Add(screens, LessonSection.Title, "Title",
            lesson.TitleCards.Count > 0 ? lesson.TitleCards : FallbackTitle(lesson),
            Math.Max(1, settings.CardsPerScreenOther), 1);
        Add(screens, LessonSection.Words, "Words", lesson.Words,
            Math.Max(2, settings.CardsPerScreenWords), Math.Max(1, Math.Min(3, settings.WordColumns)));
        Add(screens, LessonSection.Phrases, "Phrases", lesson.Phrases,
            Math.Max(1, settings.CardsPerScreenOther), 1);
        Add(screens, LessonSection.Lyrics, "Sentences", lesson.Lyrics,
            Math.Max(1, settings.CardsPerScreenOther), 1);
        return screens;
    }

    private static List<CardPair> FallbackTitle(LessonDocument lesson)
    {
        var list = new List<CardPair>();
        if (!string.IsNullOrWhiteSpace(lesson.TitleEn))
            list.Add(new CardPair(lesson.TitleEn, lesson.TitleRu));
        if (!string.IsNullOrWhiteSpace(lesson.Artist))
            list.Add(new CardPair(lesson.Artist, string.Empty));
        return list;
    }

    private static void Add(
        List<LessonScreen> screens,
        LessonSection section,
        string label,
        IList<CardPair> cards,
        int perPage,
        int columns)
    {
        if (cards == null || cards.Count == 0) return;
        for (var i = 0; i < cards.Count; i += perPage)
        {
            var page = new List<CardPair>();
            for (var j = i; j < cards.Count && j < i + perPage; j++)
                page.Add(cards[j]);
            screens.Add(new LessonScreen
            {
                Section = section,
                SectionLabel = label,
                Cards = page,
                ColumnCount = columns,
            });
        }
    }
}
