using System.IO;
using System.Text;

namespace Hermes.StrategyViewer.Services;

public sealed class StrategyLogService
{
    private readonly object _gate = new();
    private readonly string _logDir;
    private readonly string _logFile;
    private StreamWriter? _writer;

    public event Action<string>? LineAppended;

    public StrategyLogService()
    {
        _logDir = ResolveLogDirectory();
        Directory.CreateDirectory(_logDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logFile = Path.Combine(_logDir, $"strategyviewer_{stamp}.log");
        OpenWriter();
        Info($"Log directory: {_logDir}");
        Info($"Log file: {_logFile}");
    }

    public string LogDirectory => _logDir;

    public string LogFilePath => _logFile;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception ex) =>
        Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");

    public string ReadTail(int maxLines = 400)
    {
        lock (_gate)
        {
            _writer?.Flush();
        }

        if (!File.Exists(_logFile))
        {
            return string.Empty;
        }

        var lines = File.ReadLines(_logFile).ToList();
        if (lines.Count <= maxLines)
        {
            return string.Join(Environment.NewLine, lines);
        }

        return string.Join(Environment.NewLine, lines.Skip(lines.Count - maxLines));
    }

    public void DisposeWriter()
    {
        lock (_gate)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void OpenWriter()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = new StreamWriter(
                new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            _writer.WriteLine($"=== Hermes.StrategyViewer session started {DateTime.Now:O} ===");
        }
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            _writer?.WriteLine(line);
        }

        LineAppended?.Invoke(line);
    }

    private static string ResolveLogDirectory()
    {
        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                     Path.GetDirectoryName(Environment.ProcessPath) ?? "",
                 })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var docs = Path.Combine(dir.FullName, "Docs", "Logs", "StrategyViewer");
                var csproj = Path.Combine(dir.FullName, "Hermes.StrategyViewer", "Hermes.StrategyViewer.csproj");
                if (File.Exists(csproj))
                {
                    return docs;
                }

                dir = dir.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "logs");
    }
}
