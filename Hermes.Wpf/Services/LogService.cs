using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Hermes.Wpf.Services;

public sealed class LogService
{
    private readonly object _sync = new();

    /// <summary>BOM helps viewers (Notepad/PowerShell) detect UTF‑8 when Cyrillic is logged.</summary>
    private static readonly Encoding SessionFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly string _logDirectory;
    private readonly string _sessionLogFilePath;

    public LogService()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesWpf",
            "logs");

        Directory.CreateDirectory(_logDirectory);

        SessionStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"hermes_session_{SessionStamp}.log";
        _sessionLogFilePath = Path.Combine(_logDirectory, fileName);
        File.AppendAllText(_sessionLogFilePath, $"=== Session started {DateTime.Now:O} ==={Environment.NewLine}",
            SessionFileEncoding);
        DeleteOldLogs(keepLatest: 10);
    }

    /// <summary>Matches <c>hermes_session_{SessionStamp}.log</c> and sibling <c>chat_{SessionStamp}.log</c>.</summary>
    public string SessionStamp { get; }

    public ObservableCollection<string> Entries { get; } = [];

    public string CurrentLogFilePath => _sessionLogFilePath;

    public void LogInfo(string message) => Log("INFO", message);
    public void LogWarn(string message) => Log("WARN", message);
    public void LogError(string message) => Log("ERROR", message);

    public void LogTerminal(string message) => Log("TERM", message);

    private void Log(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_sync)
        {
            File.AppendAllText(_sessionLogFilePath, line + Environment.NewLine, SessionFileEncoding);
        }

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            Entries.Add(line);
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            Entries.Add(line);
        }
        else
        {
            app.Dispatcher.Invoke(() => Entries.Add(line));
        }
    }

    private void DeleteOldLogs(int keepLatest)
    {
        var logs = new DirectoryInfo(_logDirectory)
            .GetFiles("*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        foreach (var oldLog in logs.Skip(keepLatest))
        {
            try
            {
                oldLog.Delete();
            }
            catch
            {
                // Keep app stable even if OS temporarily locks a file.
            }
        }
    }
}
