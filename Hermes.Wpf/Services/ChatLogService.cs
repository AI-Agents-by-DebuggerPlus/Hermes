using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Hermes.Wpf.Services;

/// <summary>Per-project chat transcript under Logs/Hermes.Wpf/{project}/chat_{session}.log.</summary>
public sealed class ChatLogService
{
    private readonly object _sync = new();
    private readonly string _sessionStamp;
    private readonly ConcurrentDictionary<string, string> _chatFileByProject = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastChatLogPath;

    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public ChatLogService(LogService logService)
    {
        _sessionStamp = logService.SessionStamp;
        Directory.CreateDirectory(HermesLogPaths.LogsRoot);
    }

    public string CurrentChatLogPath => _lastChatLogPath ?? "(no chat logged yet)";

    public string GetChatLogPath(string projectName) =>
        _chatFileByProject.GetOrAdd(
            HermesLogPaths.SanitizeProjectFolderName(projectName),
            CreateChatLogFile);

    public void AppendMessage(string projectName, string role, string text)
    {
        var path = GetChatLogPath(projectName);
        _lastChatLogPath = path;
        var block =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{role}]{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}";

        lock (_sync)
        {
            File.AppendAllText(path, block, FileEncoding);
        }
    }

    private string CreateChatLogFile(string projectFolder)
    {
        var dir = HermesLogPaths.GetProjectDirectory(
            projectFolder == HermesLogPaths.AppFolderName ? null : projectFolder);
        var path = Path.Combine(dir, $"chat_{_sessionStamp}.log");
        File.AppendAllText(
            path,
            $"=== Chat transcript (session {_sessionStamp}) started {DateTime.Now:O} ==={Environment.NewLine}",
            FileEncoding);
        PruneOldChatLogs(dir);
        return path;
    }

    private static void PruneOldChatLogs(string projectDirectory, int keepLatest = 15)
    {
        try
        {
            var logs = new DirectoryInfo(projectDirectory)
                .GetFiles("chat_*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            foreach (var old in logs.Skip(keepLatest))
            {
                try
                {
                    old.Delete();
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch
        {
            // non-fatal
        }
    }
}
