using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>
/// Hermes CLI often writes the assistant reply on stderr; only label lines that look like real failures.
/// </summary>
public static partial class HermesCliStderrClassifier
{
    /// <summary>True when the line should be shown with <see cref="HermesCliStreamLabels.SystemErrorPrefix"/>.</summary>
    public static bool LooksLikeSystemError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var text = line.Trim();
        return ErrorCueRegex().IsMatch(text);
    }

    /// <summary>Prefixes real errors; leaves normal assistant/stderr text unchanged.</summary>
    public static string FormatForUi(string line)
    {
        if (LooksLikeSystemError(line))
        {
            return $"{HermesCliStreamLabels.SystemErrorPrefix} {line}";
        }

        return line;
    }

    // Strong cues only — avoid matching conversational phrases like "I have not found any issues".
    [GeneratedRegex(
        @"^(?:error|fatal|exception|traceback|warning)\b|" +
        @"\b(?:permission denied|access(?:\s+is)?\s+denied)\b|" +
        @"\b(?:no such file(?:\s+or\s+directory)?|file not found|path not found|directory not found|module not found|command not found)\b|" +
        @"\b(?:timed?\s*out|timeout)\b|" +
        @"\bfailed to\b|" +
        @"\b(?:ENOENT|EACCES|EPERM|ECONNREFUSED|ECONNRESET)\b|" +
        @"\bbrowser_vision\b|" +
        @"\b(?:could not|unable to)\s+(?:open|find|read|write|connect|start|load|create|access|resolve)\b|" +
        @"^\[exit\s+-?\d+\]|" +
        @"^\s*File\s+"".+?"",\s*line\s+\d+|" +
        @"^\s*at\s+\S+\.\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCueRegex();
}
