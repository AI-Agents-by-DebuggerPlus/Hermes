namespace Hermes.Wpf.Services;

/// <summary>Human-readable labels for Hermes CLI stdout/stderr lines in the UI.</summary>
public static class HermesCliStreamLabels
{
    /// <summary>
    /// Marker for stderr lines that look like real failures
    /// (see <see cref="HermesCliStderrClassifier"/>). Replaces legacy <c>[stderr]</c>.
    /// </summary>
    public const string SystemErrorPrefix = "[System error]";

    /// <summary>Legacy marker still accepted when parsing older transcripts.</summary>
    public const string LegacyStderrPrefix = "[stderr]";

    public static bool StartsWithSystemErrorMarker(string trimmedLine)
    {
        if (string.IsNullOrEmpty(trimmedLine))
        {
            return false;
        }

        return trimmedLine.StartsWith(SystemErrorPrefix, StringComparison.OrdinalIgnoreCase)
               || trimmedLine.StartsWith(LegacyStderrPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string StripSystemErrorMarker(string trimmedLine)
    {
        if (trimmedLine.StartsWith(SystemErrorPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedLine[SystemErrorPrefix.Length..].TrimStart();
        }

        if (trimmedLine.StartsWith(LegacyStderrPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedLine[LegacyStderrPrefix.Length..].TrimStart();
        }

        return trimmedLine;
    }
}
