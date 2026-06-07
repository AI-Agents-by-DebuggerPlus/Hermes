using System.Text.RegularExpressions;

namespace Hermes.Wpf.Services;

/// <summary>Parses programmatic <c>hermes chat -Q</c> output (session metadata + assistant text).</summary>
public static partial class HermesChatResponseParser
{
    private static readonly Regex SessionIdLine = SessionIdLineRegex();

    public static (string? SessionId, string DisplayText) Parse(string combinedText)
    {
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            return (null, string.Empty);
        }

        string? sessionId = null;
        var displayLines = new List<string>();

        foreach (var rawLine in combinedText.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith("[stderr]", StringComparison.Ordinal))
            {
                var stderrBody = trimmed["[stderr]".Length..].Trim();
                if (IsSessionMetadataLine(stderrBody))
                {
                    var fromStderr = TryExtractSessionId(stderrBody);
                    if (fromStderr is not null)
                    {
                        sessionId = fromStderr;
                    }

                    continue;
                }

                displayLines.Add(line);
                continue;
            }

            if (IsSessionMetadataLine(trimmed))
            {
                var id = TryExtractSessionId(trimmed);
                if (id is not null)
                {
                    sessionId = id;
                }

                continue;
            }

            displayLines.Add(line);
        }

        while (displayLines.Count > 0 && string.IsNullOrWhiteSpace(displayLines[0]))
        {
            displayLines.RemoveAt(0);
        }

        while (displayLines.Count > 0 && string.IsNullOrWhiteSpace(displayLines[^1]))
        {
            displayLines.RemoveAt(displayLines.Count - 1);
        }

        return (sessionId, string.Join(Environment.NewLine, displayLines).Trim());
    }

    public static bool IsSessionNotFound(string combinedText) =>
        combinedText.Contains("No session found", StringComparison.OrdinalIgnoreCase);

    private static bool IsSessionMetadataLine(string line) =>
        line.StartsWith("session_id:", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Resumed session", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractSessionId(string line)
    {
        var m = SessionIdLine.Match(line);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"^session_id:\s*(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SessionIdLineRegex();
}
