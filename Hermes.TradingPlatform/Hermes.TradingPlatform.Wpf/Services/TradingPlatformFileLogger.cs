using System.IO;
using System.Text;
using Hermes.TradingPlatform.Shared.Infrastructure;

namespace Hermes.TradingPlatform.Wpf.Services;

/// <summary>File log for Hermes.TradingPlatform under Logs/Hermes.TradingPlatform/.</summary>
public sealed class TradingPlatformFileLogger
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly object Sync = new();
    private static TradingPlatformFileLogger? _instance;

    private readonly string _sessionPath;

    private TradingPlatformFileLogger()
    {
        var dir = HermesLogsPaths.GetAppDirectory(HermesLogsPaths.AppTradingPlatform);
        SessionLogPruner.PruneDirectory(dir, "trading_session_*.log");
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionPath = Path.Combine(dir, $"trading_session_{stamp}.log");
        WriteLine($"=== Hermes.TradingPlatform session {DateTime.Now:O} ===");
    }

    public static TradingPlatformFileLogger Instance =>
        _instance ??= new TradingPlatformFileLogger();

    public string SessionPath => _sessionPath;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Bridge(string message) => Write("BRIDGE", message);

    public void Exchange(string message) => Write("EXCH", message);

    public void Assistant(string message) => Write("ASST", message);

    private void Write(string level, string message)
    {
        WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
    }

    private void WriteLine(string line)
    {
        lock (Sync)
        {
            File.AppendAllText(_sessionPath, line + Environment.NewLine, Utf8);
        }
    }
}
