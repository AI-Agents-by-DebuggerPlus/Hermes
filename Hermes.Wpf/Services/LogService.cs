using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Hermes.TradingPlatform.Shared.Infrastructure;

namespace Hermes.Wpf.Services;

public sealed class LogService
{
    private readonly object _sync = new();

    private static readonly Encoding SessionFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private string _sessionLogFilePath;
    private string? _activeProjectFolder;

    public LogService()
    {
        SessionStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(HermesLogPaths.LogsRoot);
        _sessionLogFilePath = BuildSessionLogPath(HermesLogPaths.AppFolderName);
        File.AppendAllText(
            _sessionLogFilePath,
            $"=== Session started {DateTime.Now:O} ==={Environment.NewLine}",
            SessionFileEncoding);
        PruneOldSessionLogs(HermesLogPaths.GetProjectDirectory(HermesLogPaths.AppFolderName));
        var pruned = PruneAllWpfSessionLogs();
        if (pruned > 0)
        {
            LogInfo(
                $"[logs] pruned {pruned} old session/chat file(s); keeping latest {SessionLogPruner.DefaultKeepLatestSessions} per folder.");
        }
    }

    public string SessionStamp { get; }

    public ObservableCollection<string> Entries { get; } = [];

    public string CurrentLogFilePath
    {
        get
        {
            lock (_sync)
            {
                return _sessionLogFilePath;
            }
        }
    }

    public void SetActiveProject(string? projectName)
    {
        var folder = HermesLogPaths.SanitizeProjectFolderName(projectName);
        lock (_sync)
        {
            if (string.Equals(_activeProjectFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _activeProjectFolder = folder;
            _sessionLogFilePath = BuildSessionLogPath(folder);
            File.AppendAllText(
                _sessionLogFilePath,
                $"=== Session log (project {folder}) {DateTime.Now:O} ==={Environment.NewLine}",
                SessionFileEncoding);
            PruneOldSessionLogs(HermesLogPaths.GetProjectDirectory(folder));
        }
    }

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

    private string BuildSessionLogPath(string projectFolder) =>
        Path.Combine(
            HermesLogPaths.GetProjectDirectory(
                projectFolder == HermesLogPaths.AppFolderName ? null : projectFolder),
            $"hermes_session_{SessionStamp}.log");

    private static void PruneOldSessionLogs(string projectDirectory) =>
        SessionLogPruner.PruneDirectory(projectDirectory, "hermes_session_*.log");

    private static int PruneAllWpfSessionLogs()
    {
        var wpfRoot = HermesLogsPaths.GetAppDirectory(HermesLogsPaths.AppHermesWpf);
        return SessionLogPruner.PruneAppTree(
            wpfRoot,
            ["hermes_session_*.log", "chat_*.log"]);
    }
}
