using System.Diagnostics;
using System.Text.RegularExpressions;
using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class WindowDisplayNames
{
    private static readonly Regex TitleSegmentRegex = new(
        @"\s*[-–—|]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        _ = NativeWindow.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return string.Empty;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string? TryGetProductName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeWindow.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var product = process.MainModule?.FileVersionInfo?.ProductName?.Trim();
            return string.IsNullOrWhiteSpace(product) ? null : product;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>«Приложение Bridge to English» when process/product is known.</summary>
    public static string FormatApplication(IntPtr hwnd, string windowTitle, string? processName = null)
    {
        processName ??= GetProcessName(hwnd);
        var product = TryGetProductName(hwnd);
        var app = ResolveApplicationLabel(processName, product, windowTitle);
        if (string.IsNullOrWhiteSpace(app))
        {
            return string.Empty;
        }

        return $"Приложение {app}";
    }

    /// <summary>«Окно Чата Hermes.Wpf» / «Окно Hermes Chat» from title and process.</summary>
    public static string FormatWindow(IntPtr hwnd, string windowTitle, string? processName = null)
    {
        processName ??= GetProcessName(hwnd);
        var label = ResolveWindowLabel(processName, windowTitle);
        return string.IsNullOrWhiteSpace(label) ? "Окно" : $"Окно {label}";
    }

    public static string FormatRegionDisplayName(
        ScreenRegionRole role,
        string roleLabel,
        string applicationDisplay,
        string windowDisplay,
        string windowTitle)
    {
        if (role == ScreenRegionRole.ApplicationWindow)
        {
            if (!string.IsNullOrWhiteSpace(applicationDisplay))
            {
                return applicationDisplay;
            }

            return string.IsNullOrWhiteSpace(windowDisplay)
                ? FormatWindow(IntPtr.Zero, windowTitle)
                : windowDisplay;
        }

        if (role == ScreenRegionRole.TaskBar)
        {
            return roleLabel;
        }

        var owner = string.IsNullOrWhiteSpace(windowDisplay)
            ? (string.IsNullOrWhiteSpace(applicationDisplay) ? windowTitle.Trim() : applicationDisplay)
            : windowDisplay;
        return string.IsNullOrWhiteSpace(owner) ? roleLabel : $"{owner} — {roleLabel}";
    }

    private static string ResolveApplicationLabel(string processName, string? productName, string windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(productName) && !IsGenericHost(productName))
        {
            return productName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(processName) && !IsGenericHost(processName))
        {
            var fromProcess = FriendlyProcessLabel(processName);
            if (!string.IsNullOrWhiteSpace(fromProcess))
            {
                return fromProcess;
            }
        }

        return ExtractApplicationFromTitle(windowTitle);
    }

    private static string ResolveWindowLabel(string processName, string windowTitle)
    {
        if (IsHermesChat(processName, windowTitle))
        {
            return "Чата Hermes.Wpf";
        }

        var title = windowTitle.Trim();
        if (title.Length == 0)
        {
            return FriendlyProcessLabel(processName);
        }

        if (!string.IsNullOrWhiteSpace(processName)
            && title.Contains(processName, StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        var shortTitle = ExtractShortWindowTitle(title);
        return shortTitle.Length > 0 ? shortTitle : title;
    }

    private static bool IsHermesChat(string processName, string windowTitle) =>
        processName.Equals("Hermes.Wpf", StringComparison.OrdinalIgnoreCase)
        && (windowTitle.Contains("Chat", StringComparison.OrdinalIgnoreCase)
            || windowTitle.Contains("Чат", StringComparison.OrdinalIgnoreCase));

    private static string ExtractApplicationFromTitle(string windowTitle)
    {
        var title = windowTitle.Trim();
        if (title.Length == 0)
        {
            return string.Empty;
        }

        var parts = TitleSegmentRegex.Split(title)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        // «file - App - Cursor» → prefer middle segment that is not a known IDE shell.
        for (var i = parts.Length - 2; i >= 1; i--)
        {
            var candidate = parts[i];
            if (candidate.Length < 2 || IsEditorShell(candidate))
            {
                continue;
            }

            return candidate;
        }

        return parts.Length >= 2 ? parts[^2] : string.Empty;
    }

    private static string ExtractShortWindowTitle(string windowTitle)
    {
        var parts = TitleSegmentRegex.Split(windowTitle.Trim())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();
        return parts.Length > 0 ? parts[0] : windowTitle.Trim();
    }

    private static bool IsEditorShell(string name) =>
        name.Contains("Cursor", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Code", StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericHost(string name) =>
        name.Contains("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
        || name.Contains("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
        || name.Contains("SystemSettings", StringComparison.OrdinalIgnoreCase)
        || name.Contains("SearchHost", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyProcessLabel(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var core = processName.Trim();
        if (core.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            core = core[..^4];
        }

        return core;
    }
}
