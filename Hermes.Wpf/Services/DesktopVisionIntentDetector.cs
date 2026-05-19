namespace Hermes.Wpf.Services;

public static class DesktopVisionIntentDetector
{
    private static readonly string[] DescribePhrases =
    [
        "опиши экран",
        "опиши что на экране",
        "что на экране",
        "подробный отчёт",
        "подробный отчет",
        "подробное описание",
        "детальное описание",
        "расскажи что на экране",
        "расскажи про экран",
        "проанализируй экран",
        "анализ экрана",
        "describe screen",
        "describe the screen",
        "what is on screen",
        "detailed report",
    ];

    public static DesktopVisionIntent Resolve(string? userRequest, string? focusWindowTarget)
    {
        if (!string.IsNullOrWhiteSpace(focusWindowTarget))
        {
            return DesktopVisionIntent.FocusWindow;
        }

        var t = (userRequest ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return DesktopVisionIntent.InternalCapture;
        }

        var lower = t.ToLowerInvariant();
        foreach (var phrase in DescribePhrases)
        {
            if (lower.Contains(phrase, StringComparison.Ordinal))
            {
                return DesktopVisionIntent.DescribeScreen;
            }
        }

        if (lower.Contains("опиши", StringComparison.Ordinal)
            && (lower.Contains("экран", StringComparison.Ordinal)
                || lower.Contains("скрин", StringComparison.Ordinal)
                || lower.Contains("снимок", StringComparison.Ordinal)))
        {
            return DesktopVisionIntent.DescribeScreen;
        }

        return DesktopVisionIntent.InternalCapture;
    }

    public static bool WantsDetailedReport(string? userRequest) =>
        Resolve(userRequest, focusWindowTarget: null) == DesktopVisionIntent.DescribeScreen;
}
