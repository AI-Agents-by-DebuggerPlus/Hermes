using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Hermes.BinanceDemoFuturesTerminal.Services;

public sealed class TerminalLogService
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly object Sync = new();
    private readonly string _sessionPath;

    public TerminalLogService()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionPath = Path.Combine(TerminalPaths.LogsDirectory, $"futures_session_{stamp}.log");
        Entries = new ObservableCollection<string>();
        WriteInternal($"=== Hermes.BinanceDemoFuturesTerminal session {DateTime.Now:O} ===");
        WriteInternal($"log file: {_sessionPath}");
    }

    public ObservableCollection<string> Entries { get; }

    public string SessionFilePath => _sessionPath;

    public event Action<string>? LineAdded;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message) =>
        WriteInternal($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");

    public void ClearView()
    {
        lock (Sync)
        {
            Entries.Clear();
        }
    }

    private void WriteInternal(string line)
    {
        lock (Sync)
        {
            File.AppendAllText(_sessionPath, line + Environment.NewLine, Utf8);
            Entries.Insert(0, line);
            if (Entries.Count > 500)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        LineAdded?.Invoke(line);
    }
}
