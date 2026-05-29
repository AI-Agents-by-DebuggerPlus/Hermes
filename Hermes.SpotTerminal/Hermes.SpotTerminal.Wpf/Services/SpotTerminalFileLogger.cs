using System.IO;
using System.Text;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Shared.Infrastructure;

namespace Hermes.SpotTerminal.Wpf.Services;

/// <summary>Session file log under Docs/Logs/SpotTerminal.</summary>
public sealed class SpotTerminalFileLogger
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly object Sync = new();
    private static SpotTerminalFileLogger? _instance;

    private readonly string _sessionPath;

    private SpotTerminalFileLogger()
    {
        var dir = SpotTerminalLogPaths.Root;
        SessionLogPruner.PruneDirectory(dir, "spot_session_*.log");
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionPath = Path.Combine(dir, $"spot_session_{stamp}.log");
        WriteLine($"=== Hermes.SpotTerminal session {DateTime.Now:O} ===");
    }

    public static SpotTerminalFileLogger Instance => _instance ??= new SpotTerminalFileLogger();

    public string SessionPath => _sessionPath;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Bridge(string message) => Write("BRIDGE", message);

    public void Exchange(string message) => Write("EXCH", message);

    public void Platform(PlatformLogEntry entry) =>
        Write(entry.EventType, $"[{entry.Source}] {entry.Message}");

    private void Write(string level, string message) =>
        WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");

    private void WriteLine(string line)
    {
        lock (Sync)
        {
            File.AppendAllText(_sessionPath, line + Environment.NewLine, Utf8);
        }
    }
}
