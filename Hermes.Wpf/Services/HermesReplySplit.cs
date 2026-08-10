using System.Text;
using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Splits a Hermes reply into readable <c>[info]</c> and AndroidChat TTS
/// (<c>[Voice]…[/Voice]</c> or legacy <c>[speak]</c>) sections.
/// See Docs/SupaBase/Формат_TTS_Android_Assistant.md and AndroidChat-Incoming-TTS-Protocol.
/// </summary>
public static class HermesReplySplit
{
    public const string InfoMarker = "[info]";
    public const string SpeakMarker = "[speak]";
    public const string VoiceOpenMarker = "[Voice]";
    public const string VoiceCloseMarker = "[/Voice]";

    private static readonly Regex SectionHeader = new(
        @"^\s*\[(info|speak)\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex VoiceBlockRegex = new(
        @"\[Voice\]\s*([\s\S]*?)\s*\[/Voice\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public readonly record struct Parts(string? Info, string? Speak, bool HasMarkers);

    public static Parts Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Parts(null, null, false);
        }

        // Prefer explicit [Voice]…[/Voice] (AndroidChat ≥ 1.0.41).
        var voiceBodies = ExtractVoiceBodies(text);
        if (voiceBodies.Count > 0)
        {
            var speakJoined = string.Join("\n", voiceBodies);
            var withoutVoice = VoiceBlockRegex.Replace(text, string.Empty);
            string? infoFromRemainder = null;
            var legacy = ParseInfoSpeakMarkers(withoutVoice);
            if (legacy.HasMarkers && !string.IsNullOrWhiteSpace(legacy.Info))
            {
                infoFromRemainder = legacy.Info;
            }
            else
            {
                var trimmed = withoutVoice.Trim();
                if (trimmed.Length > 0)
                {
                    infoFromRemainder = trimmed;
                }
            }

            return new Parts(infoFromRemainder, speakJoined, true);
        }

        return ParseInfoSpeakMarkers(text);
    }

    private static Parts ParseInfoSpeakMarkers(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        string? current = null;
        var info = new StringBuilder();
        var speak = new StringBuilder();
        var sawMarker = false;

        foreach (var rawLine in lines)
        {
            var m = SectionHeader.Match(rawLine);
            if (m.Success)
            {
                sawMarker = true;
                current = m.Groups[1].Value.Equals("info", StringComparison.OrdinalIgnoreCase)
                    ? "info"
                    : "speak";
                continue;
            }

            if (current == "info")
            {
                if (info.Length > 0)
                {
                    info.Append('\n');
                }

                info.Append(rawLine);
            }
            else if (current == "speak")
            {
                if (speak.Length > 0)
                {
                    speak.Append('\n');
                }

                speak.Append(rawLine);
            }
        }

        if (!sawMarker)
        {
            return new Parts(null, null, false);
        }

        return new Parts(TrimBody(info), TrimBody(speak), true);
    }

    /// <summary>Text for WPF / Android chat UI (prefer [info], else non-Voice text).</summary>
    public static string ForChatDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var parts = Parse(text);
        if (!parts.HasMarkers)
        {
            return text;
        }

        if (!string.IsNullOrWhiteSpace(parts.Info))
        {
            return parts.Info!;
        }

        if (!string.IsNullOrWhiteSpace(parts.Speak))
        {
            var plain = BilingualSegmentFormatter.TryExtractVoicePlainText(parts.Speak);
            return string.IsNullOrWhiteSpace(plain) ? parts.Speak! : plain;
        }

        return text.Trim();
    }

    /// <summary>
    /// Source for Android TTS / Supabase.
    /// Prefers <c>[Voice]</c> envelope (returned with tags) or legacy <c>[speak]</c> body.
    /// </summary>
    public static string ForSpeakSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var voiceBlocks = ExtractVoiceBlocksWithTags(text);
        if (voiceBlocks.Count > 0)
        {
            return string.Join("\n", voiceBlocks);
        }

        var parts = Parse(text);
        if (!parts.HasMarkers)
        {
            return text;
        }

        if (!string.IsNullOrWhiteSpace(parts.Speak))
        {
            return parts.Speak!;
        }

        return parts.Info ?? text;
    }

    public static bool ContainsVoiceEnvelope(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return VoiceBlockRegex.IsMatch(text);
    }

    private static List<string> ExtractVoiceBodies(string text)
    {
        var list = new List<string>();
        foreach (Match m in VoiceBlockRegex.Matches(text))
        {
            var body = (m.Groups[1].Value ?? string.Empty).Trim();
            if (body.Length > 0)
            {
                list.Add(body);
            }
        }

        return list;
    }

    private static List<string> ExtractVoiceBlocksWithTags(string text)
    {
        var list = new List<string>();
        foreach (Match m in VoiceBlockRegex.Matches(text))
        {
            list.Add(m.Value.Trim());
        }

        return list;
    }

    private static string? TrimBody(StringBuilder sb)
    {
        var s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}
