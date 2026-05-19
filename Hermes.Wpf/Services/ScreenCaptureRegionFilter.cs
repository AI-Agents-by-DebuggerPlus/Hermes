using Hermes.DesktopCapture.Models;

namespace Hermes.Wpf.Services;

/// <summary>Filters noisy HWND layers for chat summaries and vision prompts.</summary>
public static class ScreenCaptureRegionFilter
{
    private static readonly HashSet<string> ExcludedTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Manager",
        "Microsoft Text Input Application",
    };

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost",
    };

    public static IReadOnlyList<ScreenRegion> SelectApplicationWindows(IEnumerable<ScreenRegion> regions)
    {
        var candidates = regions
            .Where(r => r.Role == ScreenRegionRole.ApplicationWindow && r.Index > 0)
            .Where(r => !IsExcluded(r))
            .ToList();

        var picked = new List<ScreenRegion>();
        foreach (var group in candidates.GroupBy(NormalizeTitle, StringComparer.OrdinalIgnoreCase))
        {
            picked.Add(group.OrderByDescending(ScoreWindow).First());
        }

        return picked.OrderBy(r => r.Index).ToList();
    }

    public static string FormatWindowSummaryName(ScreenRegion region)
    {
        var title = (region.Label ?? region.OwnerWindowTitle ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            title = region.OwnerWindowDisplay ?? region.DisplayName;
        }

        var proc = region.OwnerProcessName?.Trim() ?? string.Empty;
        if (proc.Length == 0
            || proc.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            || title.Contains(proc, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        return $"{title} ({proc})";
    }

    private static bool IsExcluded(ScreenRegion region)
    {
        var title = (region.Label ?? region.OwnerWindowTitle ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            return true;
        }

        if (ExcludedTitles.Contains(title))
        {
            return true;
        }

        var proc = region.OwnerProcessName ?? string.Empty;
        return ExcludedProcesses.Contains(proc);
    }

    private static string NormalizeTitle(ScreenRegion region) =>
        (region.Label ?? region.OwnerWindowTitle ?? region.DisplayName).Trim();

    private static long ScoreWindow(ScreenRegion region)
    {
        var area = (long)region.Width * region.Height;
        var proc = region.OwnerProcessName ?? string.Empty;

        if (proc.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            area -= 2_000_000_000L;
        }

        if (proc.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            && (region.Label?.Contains("Program", StringComparison.OrdinalIgnoreCase) == true
                || region.Label?.StartsWith("Окно ", StringComparison.Ordinal) == true))
        {
            area -= 3_000_000_000L;
        }

        return area;
    }

    public static ScreenRegion? TryFindApplicationWindow(
        IEnumerable<ScreenRegion> regions,
        string windowHint)
    {
        var hint = windowHint.Trim();
        if (hint.Length == 0)
        {
            return null;
        }

        var windows = SelectApplicationWindows(regions);
        var exact = windows.FirstOrDefault(w =>
            TitleMatches(hint, w.Label)
            || TitleMatches(hint, w.OwnerWindowTitle)
            || TitleMatches(hint, w.OwnerProcessName));
        if (exact is not null)
        {
            return exact;
        }

        return windows.FirstOrDefault(w =>
            ContainsHint(hint, w.Label)
            || ContainsHint(hint, w.OwnerWindowTitle)
            || ContainsHint(hint, w.OwnerProcessName)
            || ContainsHint(hint, FormatWindowSummaryName(w)));
    }

    public static IReadOnlyList<ScreenRegion> SelectRegionsForWindow(
        IEnumerable<ScreenRegion> regions,
        ScreenRegion applicationWindow)
    {
        var prefix = applicationWindow.WindowPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return [applicationWindow];
        }

        return regions
            .Where(r => r.Index > 0
                        && (string.Equals(r.WindowPrefix, prefix, StringComparison.OrdinalIgnoreCase)
                            || r.Id.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.Index)
            .ToList();
    }

    public static string FormatRegionLine(ScreenRegion region)
    {
        var centerX = region.X + region.Width / 2;
        var centerY = region.Y + region.Height / 2;
        return $"{region.Index}. {region.Id} [{region.Role}] {region.Label} — {region.X},{region.Y} {region.Width}×{region.Height}, центр ≈({centerX},{centerY})";
    }

    private static bool TitleMatches(string hint, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && candidate.Trim().Equals(hint, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsHint(string hint, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && candidate.Contains(hint, StringComparison.OrdinalIgnoreCase);
}
