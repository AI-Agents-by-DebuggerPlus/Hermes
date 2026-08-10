using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Formats plain assistant text for Supabase / WordPress / Android TTS: ordered "ru" / "en" fragments without "type".
/// Preferred shape: one JSON object per sentence, lines joined by LF (see Docs/SupaBase/Формат_TTS_Android_Assistant.md).
/// </summary>
public static class BilingualSegmentFormatter
{
    private static readonly JsonSerializerOptions EscapeOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex FlashcardTypeRegex = new(
        "\"type\"\\s*:\\s*\"flashcard\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TutorTypeRegex = new(
        "\"type\"\\s*:\\s*\"tutor_(exercise|feedback|message)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SkillIntentRegex = new(
        "\"skill\"\\s*:\\s*\"flashcard_(start|stop)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Collapses 4+ spaces; preserves intentional TTS pause "   ".</summary>
    private static readonly Regex CollapseLongSpaces = new(@" {4,}", RegexOptions.Compiled);

    private static readonly Regex MarkdownHeading = new(@"^\s{0,3}#{1,6}\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownBoldItalic = new(@"(\*\*\*|___|\*\*|__|\*|_)(.+?)\1", RegexOptions.Compiled);
    private static readonly Regex MarkdownInlineCode = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex MarkdownListPrefix = new(@"^\s*(?:[-*•]|\d+[.)])\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex SentenceSplit = new(@"(?<=[\.!\?…])\s+(?=\S)", RegexOptions.Compiled);

    /// <summary>True when content must be stored verbatim (flashcards, skill JSON, already-valid Android TTS lines).</summary>
    public static bool ShouldPublishAsRawJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (HermesReplySplit.ContainsVoiceEnvelope(t)
            || LooksLikeAndroidTtsSentenceLines(t)
            || LooksLikeAndroidChatTtsProtocol(t))
        {
            return true;
        }

        if (!t.StartsWith('{') || !t.EndsWith('}'))
        {
            return false;
        }

        return FlashcardTypeRegex.IsMatch(t) || SkillIntentRegex.IsMatch(t) || TutorTypeRegex.IsMatch(t);
    }

    /// <summary>Normalize Android TTS: trim lines, drop empty.</summary>
    public static string NormalizeAndroidTtsSentenceLines(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    private static bool LooksLikeAndroidTtsSentenceLines(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Prefer multi-line TTS (one sentence per line). A single JSON object is often an agent dump.
        if (lines.Length < 2)
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (!IsAndroidTtsObjectLine(line))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// AndroidChat Incoming TTS protocol: leading <c>{"ru"/"en":…}</c> (+ optional silent trailing text).
    /// </summary>
    public static bool LooksLikeAndroidChatTtsProtocol(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !IsAndroidTtsObjectLine(lines[0]))
        {
            return false;
        }

        if (lines.Length == 1)
        {
            // Tiny status phrases only, e.g. {"ru":"скриншот создан"}.
            // Longer single blobs (multi-sentence / brands) still go through FormatPlainAsSentenceLines.
            var line = lines[0];
            if (line.Length > 90)
            {
                return false;
            }

            if (line.Contains(". ", StringComparison.Ordinal)
                || line.Contains("! ", StringComparison.Ordinal)
                || line.Contains("? ", StringComparison.Ordinal)
                || line.Contains("…", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        return true;
    }

    private static bool IsAndroidTtsObjectLine(string line)
    {
        if (!line.StartsWith('{') || !line.EndsWith('}'))
        {
            return false;
        }

        if (FlashcardTypeRegex.IsMatch(line) || SkillIntentRegex.IsMatch(line) || TutorTypeRegex.IsMatch(line))
        {
            return false;
        }

        return line.Contains("\"ru\"", StringComparison.Ordinal)
               || line.Contains("\"en\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds Android TTS payload for Supabase <c>messages.content</c>.
    /// AndroidChat ≥ 1.0.41 requires <c>[Voice]…[/Voice]</c> around TTS JSON objects.
    /// </summary>
    public static string ToSupabaseContent(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return "{}";
        }

        var trimmed = plainText.Trim();

        if (FlashcardTypeRegex.IsMatch(trimmed) || SkillIntentRegex.IsMatch(trimmed) || TutorTypeRegex.IsMatch(trimmed))
        {
            return trimmed;
        }

        // Already Voice-wrapped (agent or prior hop) — publish as-is.
        if (HermesReplySplit.ContainsVoiceEnvelope(trimmed))
        {
            return trimmed;
        }

        string inner;
        if (LooksLikeAndroidTtsSentenceLines(trimmed) || LooksLikeAndroidChatTtsProtocol(trimmed))
        {
            inner = NormalizeAndroidTtsSentenceLines(trimmed);
        }
        else if (trimmed.StartsWith('{')
                 && LooksLikeBilingualPayload(trimmed)
                 && !trimmed.Contains('\n'))
        {
            // Single bilingual JSON object (often one huge "ru" with markdown) → extract and reformat.
            var extracted = TryExtractSingleObjectVoicePlainText(trimmed);
            inner = !string.IsNullOrWhiteSpace(extracted)
                ? FormatPlainAsSentenceLines(extracted)
                : FormatPlainAsSentenceLines(trimmed);
        }
        else
        {
            inner = FormatPlainAsSentenceLines(trimmed);
        }

        return EnsureVoiceEnvelope(inner);
    }

    /// <summary>Wraps TTS JSON lines in <c>[Voice]…[/Voice]</c> when missing (AndroidChat ≥ 1.0.41).</summary>
    public static string EnsureVoiceEnvelope(string? ttsBody)
    {
        if (string.IsNullOrWhiteSpace(ttsBody))
        {
            return "{}";
        }

        var t = ttsBody.Trim();
        if (HermesReplySplit.ContainsVoiceEnvelope(t)
            || FlashcardTypeRegex.IsMatch(t)
            || SkillIntentRegex.IsMatch(t)
            || TutorTypeRegex.IsMatch(t))
        {
            return t;
        }

        if (t is "{}" or "[]")
        {
            return t;
        }

        return "[Voice]\n" + t + "\n[/Voice]";
    }

    /// <summary>Strip markdown, split sentences, emit one TTS JSON object per sentence.</summary>
    public static string FormatPlainAsSentenceLines(string plainText)
    {
        var cleaned = StripMarkdownForTts(plainText);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "{}";
        }

        var sentences = SplitIntoSentences(cleaned);
        if (sentences.Count == 0)
        {
            return "{}";
        }

        var lines = new List<string>();
        foreach (var sentence in sentences)
        {
            var obj = BuildSentenceObject(sentence);
            if (obj.Length > 2)
            {
                lines.Add(obj);
            }
        }

        return lines.Count == 0 ? "{}" : string.Join('\n', lines);
    }

    private static string BuildSentenceObject(string sentence)
    {
        var segments = CoalesceAdjacent(SegmentByScript(sentence));
        segments = SanitizeVoiceSegments(segments);
        if (segments.Count == 0)
        {
            return "{}";
        }

        if (segments.Count == 1)
        {
            return BuildSingleLanguageObject(segments[0].Text, segments[0].Lang);
        }

        var sb = new StringBuilder();
        sb.Append('{');
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var (lang, fragment) = segments[i];
            sb.Append(JsonSerializer.Serialize(lang, EscapeOptions));
            sb.Append(':');
            sb.Append(JsonSerializer.Serialize(fragment, EscapeOptions));
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildSingleLanguageObject(string text, string lang) =>
        $"{{{JsonSerializer.Serialize(lang, EscapeOptions)}:{JsonSerializer.Serialize(text, EscapeOptions)}}}";

    /// <summary>Removes decorative punctuation; drops empty fragments.</summary>
    private static List<(string Lang, string Text)> SanitizeVoiceSegments(List<(string Lang, string Text)> segments)
    {
        var result = new List<(string Lang, string Text)>();
        foreach (var (lang, text) in segments)
        {
            var clean = SanitizeVoiceFragment(text);
            if (clean.Length == 0)
            {
                continue;
            }

            result.Add((lang, clean));
        }

        return CoalesceAdjacent(result);
    }

    private static string SanitizeVoiceFragment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var s = text
            .Replace("«", string.Empty, StringComparison.Ordinal)
            .Replace("»", string.Empty, StringComparison.Ordinal)
            .Replace("„", string.Empty, StringComparison.Ordinal)
            .Replace("“", string.Empty, StringComparison.Ordinal)
            .Replace("”", string.Empty, StringComparison.Ordinal)
            .Replace("•", ", ", StringComparison.Ordinal)
            .Replace("·", ", ", StringComparison.Ordinal)
            .Replace("—", " ", StringComparison.Ordinal)
            .Replace("–", " ", StringComparison.Ordinal)
            .Replace("-", " ", StringComparison.Ordinal);

        s = CollapseLongSpaces.Replace(s, " ").Trim();
        return s;
    }

    internal static string StripMarkdownForTts(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var s = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        s = MarkdownHeading.Replace(s, string.Empty);
        s = MarkdownListPrefix.Replace(s, string.Empty);
        s = MarkdownLink.Replace(s, "$1");
        s = MarkdownInlineCode.Replace(s, "$1");
        // Repeated passes for nested **_x_** style; usually one is enough.
        for (var i = 0; i < 3; i++)
        {
            var next = MarkdownBoldItalic.Replace(s, "$2");
            if (next == s)
            {
                break;
            }

            s = next;
        }

        s = s.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal);
        s = CollapseLongSpaces.Replace(s, " ");
        return s.Trim();
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var result = new List<string>();
        foreach (var para in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (para.Length == 0)
            {
                continue;
            }

            var parts = SentenceSplit.Split(para);
            foreach (var part in parts)
            {
                var t = part.Trim();
                if (t.Length > 0)
                {
                    result.Add(t);
                }
            }
        }

        return result;
    }

    private static List<(string Lang, string Text)> CoalesceAdjacent(List<(string Lang, string Text)> segments)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var merged = new List<(string Lang, string Text)>();
        foreach (var seg in segments)
        {
            if (merged.Count > 0 && merged[^1].Lang == seg.Lang)
            {
                var prev = merged[^1];
                merged[^1] = (prev.Lang, prev.Text + seg.Text);
            }
            else
            {
                merged.Add(seg);
            }
        }

        return merged;
    }

    /// <summary>Plain voice text from a single-language or ru/en JSON object (no "type" / skill keys).</summary>
    public static string? TryExtractVoicePlainText(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var t = content.Trim();
        if (!t.StartsWith("{", StringComparison.Ordinal))
        {
            return t;
        }

        if (LooksLikeAndroidTtsSentenceLines(t))
        {
            var parts = new List<string>();
            foreach (var line in t.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var piece = TryExtractSingleObjectVoicePlainText(line);
                if (!string.IsNullOrWhiteSpace(piece))
                {
                    parts.Add(piece);
                }
            }

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        return TryExtractSingleObjectVoicePlainText(t);
    }

    private static string? TryExtractSingleObjectVoicePlainText(string t)
    {
        // Prefer ordered regex extraction (duplicate keys). JsonDocument keeps only last key.
        var ordered = ExtractOrderedRuEnFragments(t);
        if (ordered.Count > 0)
        {
            return string.Join(" ", ordered.Select(f => f.Text));
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (doc.RootElement.TryGetProperty("type", out _))
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("ru") || prop.NameEquals("en"))
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        parts.Add(v);
                    }
                }
                else
                {
                    return null;
                }
            }

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex OrderedRuEn = new(
        "\"(ru|en)\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static List<(string Lang, string Text)> ExtractOrderedRuEnFragments(string jsonObject)
    {
        var list = new List<(string Lang, string Text)>();
        foreach (Match m in OrderedRuEn.Matches(jsonObject))
        {
            var lang = m.Groups[1].Value;
            var raw = m.Groups[2].Value;
            try
            {
                var text = JsonSerializer.Deserialize<string>("\"" + raw + "\"", EscapeOptions);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add((lang, text));
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    list.Add((lang, raw));
                }
            }
        }

        return list;
    }

    private static bool LooksLikeBilingualPayload(string json)
    {
        if (json.Contains("\"type\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return json.Contains("\"ru\"", StringComparison.Ordinal)
               || json.Contains("\"en\"", StringComparison.Ordinal);
    }

    private static List<(string Lang, string Text)> SegmentByScript(string text)
    {
        var result = new List<(string Lang, string Text)>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var currentLang = (string?)null;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (currentLang is null || buffer.Length == 0)
            {
                buffer.Clear();
                return;
            }

            var piece = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(piece) || piece.Contains('\n'))
            {
                result.Add((currentLang, piece));
            }

            buffer.Clear();
        }

        foreach (var ch in text)
        {
            var lang = ClassifyChar(ch);
            if (lang is null)
            {
                buffer.Append(ch);
                continue;
            }

            if (currentLang is not null && lang != currentLang)
            {
                Flush();
            }

            currentLang = lang;
            buffer.Append(ch);
        }

        Flush();
        return result;
    }

    private static string? ClassifyChar(char ch)
    {
        if (IsCyrillic(ch))
        {
            return "ru";
        }

        // Digits stay neutral so "5 мин" stays in the Russian segment.
        // Explicit ASCII letters (avoid relying on IsAsciiLetter across TFMs).
        if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
        {
            return "en";
        }

        return null;
    }

    private static bool IsCyrillic(char ch) =>
        ch is >= '\u0400' and <= '\u04FF'
        or >= '\u0500' and <= '\u052F';
}
