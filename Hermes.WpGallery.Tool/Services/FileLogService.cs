using System.IO;
using System.Text;
using Hermes.WpGallery.Tool.Models;

namespace Hermes.WpGallery.Tool.Services;

public class FileLogService
{
    private readonly object _lock = new();

    public string LogsDirectory { get; }
    public string ScreenshotsDirectory { get; }

    public FileLogService()
    {
        LogsDirectory = ResolveLogsDirectory();
        ScreenshotsDirectory = Path.Combine(LogsDirectory, "screenshots");
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ScreenshotsDirectory);
    }

    public void Write(LogLevel level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}] {message}";
        var path = Path.Combine(LogsDirectory, $"hermes_{DateTime.Now:yyyy-MM-dd}.log");

        lock (_lock)
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public string SaveScreenshot(byte[] data, string filename)
    {
        var safeName = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(ScreenshotsDirectory, safeName);
        lock (_lock)
        {
            File.WriteAllBytes(path, data);
        }
        return path;
    }

    private static string ResolveLogsDirectory()
    {
        var root = Environment.GetEnvironmentVariable("HERMES_LOGS_ROOT")?.Trim();
        if (string.IsNullOrEmpty(root))
        {
            root = @"D:\Programming\AI_Agents\Hermes\Logs";
        }

        return Path.Combine(root, "Hermes.WpGallery.Tool");
    }
}
