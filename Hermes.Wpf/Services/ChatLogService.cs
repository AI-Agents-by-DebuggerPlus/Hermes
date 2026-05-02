using System.IO;
using System.Text;

namespace Hermes.Wpf.Services;

/// <summary>UTF-8 chat transcript (user-visible lines only) in a file separate from the session/terminal log.</summary>
public sealed class ChatLogService
{
    private readonly object _sync = new();

    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly string _filePath;

    public ChatLogService(LogService logService)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesWpf",
            "chat_logs");
        Directory.CreateDirectory(root);
        _filePath = Path.Combine(root, $"chat_{logService.SessionStamp}.log");
        File.AppendAllText(
            _filePath,
            $"=== Chat transcript (session {logService.SessionStamp}) started {DateTime.Now:O} ==={Environment.NewLine}",
            FileEncoding);

        DeleteOldChatLogs(root, keepLatest: 12);
    }

    private static void DeleteOldChatLogs(string directory, int keepLatest)
    {
        try
        {
            var logs = new DirectoryInfo(directory)
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
                    // ignore locked files
                }
            }
        }
        catch
        {
            // non-fatal
        }
    }

    public string CurrentChatLogPath => _filePath;

    public void AppendMessage(string projectName, string role, string text)
    {
        var safeProject = string.IsNullOrEmpty(projectName) ? "(no project)" : projectName;
        var block =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{role}] project={safeProject}{Environment.NewLine}{text}{Environment.NewLine}{Environment.NewLine}";

        lock (_sync)
        {
            File.AppendAllText(_filePath, block, FileEncoding);
        }
    }
}
