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
        EnsureParentDirectory(_sessionLogFilePath);
        File.AppendAllText(
            _sessionLogFilePath,
            $"=== Session started {DateTime.Now:O} ==={Environment.NewLine}",
            SessionFileEncoding);
        PruneOldSessionLogs(HermesLogPaths.GetProjectDirectory(HermesLogPaths.AppFolderName));
        var pruned = PruneAllWpfSessionLogs();
        if (pruned > 0)
        {
            LogInfo(
                $"[logs] pruned {pruned} old session/chat file(s); keeping latest {HermesLogPaths.RetainedLogSessionCount} per folder.");
        }
    }

    public string SessionStamp { get; }

    public ObservableCollection<string> Entries { get; } = [];

    /// <summary>Raised after a line is written (UI thread when Dispatcher is available).</summary>
    public event Action<string, string>? LineLogged;

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
            EnsureParentDirectory(_sessionLogFilePath);
            File.AppendAllText(
                _sessionLogFilePath,
                $"=== Session log (project {folder}) {DateTime.Now:O} ==={Environment.NewLine}",
                SessionFileEncoding);
            PruneOldSessionLogs(HermesLogPaths.GetProjectDirectory(folder));
        }
    }

    /// <summary>
    /// After project rename moves Logs/Hermes.Wpf/{old} → {new}, retarget the open session file
    /// so subsequent Log* calls do not write into a deleted directory.
    /// </summary>
    public void RetargetAfterProjectRename(string oldName, string newName)
    {
        var oldFolder = HermesLogPaths.SanitizeProjectFolderName(oldName);
        var newFolder = HermesLogPaths.SanitizeProjectFolderName(newName);
        if (string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
            return;

        lock (_sync)
        {
            if (!string.Equals(_activeProjectFolder, oldFolder, StringComparison.OrdinalIgnoreCase))
                return;

            var fileName = Path.GetFileName(_sessionLogFilePath);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"hermes_session_{SessionStamp}.log";

            _activeProjectFolder = newFolder;
            _sessionLogFilePath = Path.Combine(
                HermesLogPaths.GetProjectDirectory(newFolder),
                fileName);
            EnsureParentDirectory(_sessionLogFilePath);
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
            EnsureParentDirectory(_sessionLogFilePath);
            File.AppendAllText(_sessionLogFilePath, line + Environment.NewLine, SessionFileEncoding);
        }

        void PublishUi()
        {
            Entries.Add(line);
            LineLogged?.Invoke(level, line);
        }

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null)
        {
            PublishUi();
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            PublishUi();
        }
        else
        {
            app.Dispatcher.Invoke(PublishUi);
        }
    }

    private static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private string BuildSessionLogPath(string projectFolder) =>
        Path.Combine(
            HermesLogPaths.GetProjectDirectory(
                projectFolder == HermesLogPaths.AppFolderName ? null : projectFolder),
            $"hermes_session_{SessionStamp}.log");

    private static void PruneOldSessionLogs(string projectDirectory) =>
        SessionLogPruner.PruneDirectory(
            projectDirectory,
            "hermes_session_*.log",
            HermesLogPaths.RetainedLogSessionCount);

    private static int PruneAllWpfSessionLogs()
    {
        var wpfRoot = HermesLogsPaths.GetAppDirectory(HermesLogsPaths.AppHermesWpf);
        return SessionLogPruner.PruneAppTree(
            wpfRoot,
            ["hermes_session_*.log", "chat_*.log"],
            HermesLogPaths.RetainedLogSessionCount);
    }
}
