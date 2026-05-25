using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Formats plain assistant text for Supabase / WordPress / Android TTS: ordered "ru" / "en" fragments without "type".
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

    private static readonly Regex SkillIntentRegex = new(
        "\"skill\"\\s*:\\s*\"flashcard_(start|stop)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CollapseSpaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Collapses 4+ spaces; preserves intentional TTS pause "   ".</summary>
    private static readonly Regex CollapseLongSpaces = new(@" {4,}", RegexOptions.Compiled);

    /// <summary>True when content must be stored verbatim (flashcards, skill JSON, Android TTS lines).</summary>
    public static bool ShouldPublishAsRawJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (LooksLikeAndroidTtsSentenceLines(t))
        {
            return true;
        }

        if (!t.StartsWith('{') || !t.EndsWith('}'))
        {
            return false;
        }

        if (FlashcardTypeRegex.IsMatch(t) || SkillIntentRegex.IsMatch(t))
        {
            return true;
        }

        return LooksLikeBilingualPayload(t);
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
        if (lines.Length == 0)
        {
            return false;
        }

        foreach (var line in lines)
        {
            if (!line.StartsWith('{') || !line.EndsWith('}'))
            {
                return false;
            }

            if (FlashcardTypeRegex.IsMatch(line) || SkillIntentRegex.IsMatch(line))
            {
                return false;
            }

            if (!line.Contains("\"ru\"", StringComparison.Ordinal) && !line.Contains("\"en\"", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds {"ru":"…","en":"…",…} with duplicate keys allowed (manual JSON).</summary>
    public static string ToSupabaseContent(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return "{}";
        }

        var trimmed = plainText.Trim();
        if (ShouldPublishAsRawJson(plainText))
        {
            return LooksLikeAndroidTtsSentenceLines(trimmed)
                ? NormalizeAndroidTtsSentenceLines(trimmed)
                : trimmed;
        }

        var segments = CoalesceAdjacent(SegmentByScript(trimmed));
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
            .Replace("-", " ", StringComparison.Ordinal);

        s = CollapseLongSpaces.Replace(s, " ").Trim();

        // Trailing orphan punctuation after guillemet removal (e.g. "Запланировано:" → keep colon for natural pause)
        return s;
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

            currentLang ??= lang;
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
        if (char.IsAsciiLetter(ch))
        {
            return "en";
        }

        return null;
    }

    private static bool IsCyrillic(char ch) =>
        ch is >= '\u0400' and <= '\u04FF'
        or >= '\u0500' and <= '\u052F';
}

