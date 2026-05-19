using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hermes.DesktopCapture.Models;

namespace Hermes.DesktopCapture;

public static class ScreenCapturePipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ScreenCaptureResult CaptureMonitor(
        string outputDirectory,
        int monitorIndex = -1,
        string? fileNameStamp = null)
    {
        var monitor = MonitorCapture.ResolveMonitor(monitorIndex);
        Directory.CreateDirectory(outputDirectory);

        var stamp = fileNameStamp ?? DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var imagePath = Path.Combine(outputDirectory, $"screen_{stamp}.png");
        var annotatedPath = Path.Combine(outputDirectory, $"screen_{stamp}_regions.png");
        var metadataPath = Path.Combine(outputDirectory, $"screen_{stamp}.json");

        MonitorCapture.CaptureToFile(monitor, imagePath);

        var hwnd = NativeWindow.GetForegroundWindow();
        var windowTitle = NativeWindow.GetWindowTitle(hwnd);
        var regions = WindowRegionAnalyzer.AnalyzeMonitor(monitor.Bounds);
        var windowCount = regions.Count(r => r.Role == ScreenRegionRole.ApplicationWindow);
        CaptureAnnotator.SaveAnnotated(imagePath, annotatedPath, monitor, regions);

        var metadata = new
        {
            capturedAt = DateTimeOffset.Now,
            monitor = new
            {
                monitor.Index,
                monitor.DeviceName,
                monitor.X,
                monitor.Y,
                monitor.Width,
                monitor.Height,
                monitor.IsPrimary,
            },
            foregroundWindowTitle = windowTitle,
            windowCount,
            imagePath,
            annotatedImagePath = annotatedPath,
            regions = regions.Select(r => new
            {
                r.Index,
                r.Id,
                role = r.Role.ToString(),
                r.Label,
                r.DisplayName,
                r.OwnerApplicationDisplay,
                r.OwnerWindowDisplay,
                r.OwnerWindowTitle,
                r.OwnerProcessName,
                r.WindowPrefix,
                r.X,
                r.Y,
                r.Width,
                r.Height,
            }),
        };

        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));

        return new ScreenCaptureResult
        {
            ImagePath = imagePath,
            AnnotatedImagePath = annotatedPath,
            MetadataPath = metadataPath,
            Monitor = monitor,
            Regions = regions,
            ForegroundWindowTitle = windowTitle,
            WindowCount = windowCount,
            CapturedAt = DateTimeOffset.Now,
        };
    }
}
