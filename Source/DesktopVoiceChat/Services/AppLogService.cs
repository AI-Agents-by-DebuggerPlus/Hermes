using System.IO;

namespace DesktopVoiceChat.Services;

/// <summary>
/// Пишет строки в файл в каталоге Logs и рассылает копию подписчикам (окно журнала).
/// </summary>
public static class AppLogService
{
    private static readonly object Sync = new();

    /// <summary>Каталог логов (как запросил пользователь).</summary>
    public const string LogDirectory =
        @"D:\Programming\Cursor\2026\WPF\April\AndroidVoiceClient\src\DesktopVoiceChat\Logs";

    private static string? _filePath;
    private static bool _initialized;

    public static event EventHandler<string>? MessageLogged;

    public static string? CurrentLogFilePath
    {
        get
        {
            lock (Sync)
            {
                return _filePath;
            }
        }
    }

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(LogDirectory);
            _filePath = Path.Combine(
                LogDirectory,
                $"desktop-voice-chat-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(
                _filePath,
                $"{DateTime.Now:O} Session started{Environment.NewLine}");
            _initialized = true;
        }
    }

    public static void Log(string message, string? category = null)
    {
        if (!_initialized)
        {
            Initialize();
        }

        var line =
            $"{DateTime.Now:HH:mm:ss.fff} {(category is null ? "" : $"[{category}] ")}{message}";

        lock (Sync)
        {
            if (_filePath is not null)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }

        MessageLogged?.Invoke(null, line);
    }
}
