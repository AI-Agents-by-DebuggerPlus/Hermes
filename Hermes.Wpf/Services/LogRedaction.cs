namespace Hermes.Wpf.Services;

/// <summary>Safe fragments for session logs (no full secrets).</summary>
public static class LogRedaction
{
    public static string MaskApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "(empty)";
        }

        var t = key.Trim();
        if (t.Length <= 10)
        {
            return "***";
        }

        return $"{t[..4]}…{t[^4..]} (len={t.Length})";
    }

    public static string SupabaseHostForLog(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(no url)";
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "(invalid url)";
        }

        return uri.Host;
    }
}
