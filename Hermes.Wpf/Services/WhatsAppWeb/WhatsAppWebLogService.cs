using System.IO;
using System.Text;
using Hermes.TradingPlatform.Shared.Infrastructure;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Services.WhatsAppWeb;

/// <summary>WhatsApp Web monitor logs under Docs/Logs/WhatsAppWeb.</summary>
public sealed class WhatsAppWebLogService
{
    private readonly object _sync = new();
    private readonly string _logFilePath;

    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public WhatsAppWebLogService(LogService sessionLog)
    {
        var dir = HermesLogPaths.GetWhatsAppLogsDirectory();
        _logFilePath = Path.Combine(dir, $"whatsapp_{sessionLog.SessionStamp}.log");
        File.AppendAllText(
            _logFilePath,
            $"=== WhatsApp Web log (session {sessionLog.SessionStamp}) started {DateTime.Now:O} ==={Environment.NewLine}",
            FileEncoding);
        PruneOldLogs(dir);
    }

    public string CurrentLogFilePath => _logFilePath;

    public void LogInfo(string message) => Log("INFO", message);

    public void LogWarn(string message) => Log("WARN", message);

    public void LogError(string message) => Log("ERROR", message);

    private void Log(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_sync)
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, FileEncoding);
        }
    }

    private static void PruneOldLogs(string directory) =>
        SessionLogPruner.PruneDirectory(
            directory,
            "whatsapp_*.log",
            HermesLogPaths.RetainedLogSessionCount);
}
