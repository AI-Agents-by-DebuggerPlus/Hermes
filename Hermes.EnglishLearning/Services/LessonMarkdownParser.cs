using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hermes.EnglishLearning.Models;

namespace Hermes.EnglishLearning.Services;

/// <summary>
/// Parses Hermes lesson markdown.
/// Sections: ## title | ## words | ## lyrics
/// Card lines: English | Russian   OR   English then next line Russian (lyrics blocks separated by ---).
/// </summary>
public static class LessonMarkdownParser
{
    public static LessonDocument ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Lesson markdown not found.", path);
        }

        return Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static LessonDocument Parse(string markdown)
    {
        var doc = new LessonDocument();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;

        // Optional YAML-ish front matter between ---
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                ParseMetaLine(doc, lines[i]);
                i++;
            }

            if (i < lines.Length && lines[i].Trim() == "---")
            {
                i++;
            }
        }

        string? section = null;
        var buffer = new List<string>();

        void FlushSection()
        {
            if (section == null)
            {
                return;
            }

            var cards = ParseSectionCards(section, buffer);
            switch (section)
            {
                case "title":
                    foreach (var c in cards)
                    {
                        doc.TitleCards.Add(c);
                    }

                    if (string.IsNullOrWhiteSpace(doc.TitleEn) && cards.Count > 0)
                    {
                        doc.TitleEn = cards[0].En;
                        doc.TitleRu = cards[0].Ru;
                    }

                    break;
                case "words":
                    doc.Words.AddRange(cards);
                    break;
                case "lyrics":
                    doc.Lyrics.AddRange(cards);
                    break;
            }

            buffer.Clear();
        }

        for (; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trim = raw.Trim();

            if (trim.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushSection();
                section = NormalizeSection(trim.Substring(3));
                continue;
            }

            // Skip top-level # title if present
            if (trim.StartsWith("# ", StringComparison.Ordinal) && section == null)
            {
                if (string.IsNullOrWhiteSpace(doc.TitleEn))
                {
                    doc.TitleEn = trim.Substring(2).Trim();
                }

                continue;
            }

            if (section != null)
            {
                buffer.Add(raw);
            }
        }

        FlushSection();

        if (doc.TitleCards.Count == 0 && !string.IsNullOrWhiteSpace(doc.TitleEn))
        {
            doc.TitleCards.Add(new CardPair(doc.TitleEn, doc.TitleRu));
            if (!string.IsNullOrWhiteSpace(doc.Artist))
            {
                doc.TitleCards.Add(new CardPair(doc.Artist, string.Empty));
            }
        }

        return doc;
    }

    private static void ParseMetaLine(LessonDocument doc, string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || !t.Contains(':'))
        {
            return;
        }

        var idx = t.IndexOf(':');
        var key = t.Substring(0, idx).Trim().ToLowerInvariant();
        var val = t.Substring(idx + 1).Trim().Trim('"');
        switch (key)
        {
            case "title":
            case "title_en":
                doc.TitleEn = val;
                break;
            case "title_ru":
                doc.TitleRu = val;
                break;
            case "artist":
                doc.Artist = val;
                break;
        }
    }

    private static string NormalizeSection(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.StartsWith("title", StringComparison.Ordinal))
        {
            return "title";
        }

        if (n.StartsWith("word", StringComparison.Ordinal) || n.StartsWith("vocab", StringComparison.Ordinal))
        {
            return "words";
        }

        if (n.StartsWith("lyric", StringComparison.Ordinal) || n.StartsWith("phrase", StringComparison.Ordinal)
            || n.StartsWith("line", StringComparison.Ordinal))
        {
            return "lyrics";
        }

        return n;
    }

    private static List<CardPair> ParseSectionCards(string section, List<string> lines)
    {
        if (section == "lyrics")
        {
            return ParseLyricsBlocks(lines);
        }

        return ParsePipeOrPairLines(lines);
    }

    private static List<CardPair> ParsePipeOrPairLines(IEnumerable<string> lines)
    {
        var result = new List<CardPair>();
        string? pendingEn = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line == "---")
            {
                continue;
            }

            var pipe = line.IndexOf('|');
            if (pipe >= 0)
            {
                var en = line.Substring(0, pipe).Trim();
                var ru = line.Substring(pipe + 1).Trim();
                if (en.Length > 0)
                {
                    result.Add(new CardPair(en, ru));
                }

                pendingEn = null;
                continue;
            }

            if (pendingEn == null)
            {
                pendingEn = line;
            }
            else
            {
                result.Add(new CardPair(pendingEn, line));
                pendingEn = null;
            }
        }

        if (pendingEn != null)
        {
            result.Add(new CardPair(pendingEn, string.Empty));
        }

        return result;
    }

    private static List<CardPair> ParseLyricsBlocks(List<string> lines)
    {
        var result = new List<CardPair>();
        var blocks = new List<List<string>>();
        var current = new List<string>();

        foreach (var raw in lines)
        {
            var trim = raw.Trim();
            if (trim == "---")
            {
                if (current.Count > 0)
                {
                    blocks.Add(current);
                    current = new List<string>();
                }

                continue;
            }

            if (trim.Length == 0)
            {
                continue;
            }

            if (trim.StartsWith("#", StringComparison.Ordinal) || trim.StartsWith("**", StringComparison.Ordinal))
            {
                continue;
            }

            current.Add(trim);
        }

        if (current.Count > 0)
        {
            blocks.Add(current);
        }

        foreach (var block in blocks)
        {
            if (block.Count == 0)
            {
                continue;
            }

            // Prefer: first half EN, second half RU when even count and no pipes
            if (block.All(l => !l.Contains("|")) && block.Count >= 2 && block.Count % 2 == 0)
            {
                var half = block.Count / 2;
                var en = string.Join("\n", block.Take(half));
                var ru = string.Join("\n", block.Skip(half));
                result.Add(new CardPair(en, ru));
                continue;
            }

            // Lines with pipes, or EN then RU alternating pairs
            if (block.Any(l => l.Contains("|")))
            {
                result.AddRange(ParsePipeOrPairLines(block));
                continue;
            }

            if (block.Count == 2)
            {
                result.Add(new CardPair(block[0], block[1]));
            }
            else if (block.Count == 1)
            {
                result.Add(new CardPair(block[0], string.Empty));
            }
            else
            {
                // Odd: treat consecutive pairs
                for (var i = 0; i + 1 < block.Count; i += 2)
                {
                    result.Add(new CardPair(block[i], block[i + 1]));
                }

                if (block.Count % 2 == 1)
                {
                    result.Add(new CardPair(block[block.Count - 1], string.Empty));
                }
            }
        }

        return result;
    }
}
