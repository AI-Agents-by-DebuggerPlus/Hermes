using System.IO;
using Hermes.DesktopCapture;
using Hermes.DesktopCapture.Models;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class DesktopScreenCaptureService
{
    private readonly Func<HermesSettings> _settings;
    private readonly LogService _log;

    public DesktopScreenCaptureService(LogService log, Func<HermesSettings> settings)
    {
        _log = log;
        _settings = settings;
    }

    public ScreenCaptureResult CapturePrimaryMonitor()
    {
        var primaryDir = GetPrimaryOutputDirectory();
        var duplicateDir = GetDuplicateOutputDirectory();
        DeletePreviousCaptures(primaryDir, duplicateDir);

        var index = _settings().DesktopScreenshotMonitorIndex;
        _log.LogInfo($"[screen-capture] monitor index={index}, primary dir={primaryDir}");

        var result = ScreenCapturePipeline.CaptureMonitor(primaryDir, index);

        if (duplicateDir is null)
        {
            return result;
        }

        try
        {
            Directory.CreateDirectory(duplicateDir);
            DuplicateFile(result.ImagePath, duplicateDir);
            DuplicateFile(result.AnnotatedImagePath, duplicateDir);
            DuplicateFile(result.MetadataPath, duplicateDir);
            _log.LogInfo($"[screen-capture] duplicate copy → {duplicateDir}");
            return new ScreenCaptureResult
            {
                ImagePath = result.ImagePath,
                AnnotatedImagePath = result.AnnotatedImagePath,
                MetadataPath = result.MetadataPath,
                Monitor = result.Monitor,
                Regions = result.Regions,
                ForegroundWindowTitle = result.ForegroundWindowTitle,
                WindowCount = result.WindowCount,
                DuplicateDirectory = duplicateDir,
                CapturedAt = result.CapturedAt,
            };
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[screen-capture] duplicate copy failed: {ex.Message}");
            return result;
        }
    }

    /// <summary>Primary storage (always default LocalAppData path).</summary>
    public static string GetPrimaryOutputDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "screenshots");

    private string? GetDuplicateOutputDirectory()
    {
        var configured = _settings().DesktopScreenshotDirectory?.Trim();
        return string.IsNullOrEmpty(configured) ? null : configured;
    }

    private static void DuplicateFile(string sourcePath, string duplicateDir)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var dest = Path.Combine(duplicateDir, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, dest, overwrite: true);
    }

    private void DeletePreviousCaptures(string primaryDir, string? duplicateDir)
    {
        var removed = 0;
        removed += DeleteScreenCaptureFilesIn(primaryDir);
        if (!string.IsNullOrWhiteSpace(duplicateDir))
        {
            removed += DeleteScreenCaptureFilesIn(duplicateDir);
        }

        if (removed > 0)
        {
            _log.LogInfo($"[screen-capture] removed {removed} previous file(s) before capture");
        }
    }

    private static int DeleteScreenCaptureFilesIn(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var count = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "screen_*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(path);
                count++;
            }
            catch
            {
                // non-fatal — new capture may overwrite same stamp on retry
            }
        }

        return count;
    }
}
