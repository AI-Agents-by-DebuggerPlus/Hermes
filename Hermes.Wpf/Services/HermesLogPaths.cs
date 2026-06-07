using System.IO;
using Hermes.TradingPlatform.Shared.Infrastructure;

namespace Hermes.Wpf.Services;

/// <summary>Wpf log paths: general session under Logs/Hermes.Wpf; chat and WhatsApp under Docs/Logs.</summary>
public static class HermesLogPaths
{
    public const string AppFolderName = HermesLogsPaths.WpfNoProjectFolder;

    public const string ChatLogsRoot = @"D:\Programming\AI_Agents\Hermes\Docs\Logs\HermesWpfChat";

    public const string WhatsAppLogsRoot = @"D:\Programming\AI_Agents\Hermes\Docs\Logs\WhatsAppWeb";

    public static string LogsRoot => HermesLogsPaths.Root;

    public static string GetProjectDirectory(string? projectName) =>
        HermesLogsPaths.GetWpfProjectDirectory(projectName);

    public static string GetChatProjectDirectory(string? projectName)
    {
        var sub = SanitizeProjectFolderName(projectName);
        if (string.IsNullOrEmpty(sub))
        {
            sub = AppFolderName;
        }

        var dir = Path.Combine(ChatLogsRoot, sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetWhatsAppLogsDirectory()
    {
        Directory.CreateDirectory(WhatsAppLogsRoot);
        return WhatsAppLogsRoot;
    }

    public static string SanitizeProjectFolderName(string? projectName)
    {
        var safe = HermesLogsPaths.SanitizeFolderName(projectName);
        return string.IsNullOrEmpty(safe) ? AppFolderName : safe;
    }

    /// <summary>Keep chat/session logs for the current run plus two prior sessions (3 files per folder).</summary>
    public const int RetainedLogSessionCount = 3;
}
