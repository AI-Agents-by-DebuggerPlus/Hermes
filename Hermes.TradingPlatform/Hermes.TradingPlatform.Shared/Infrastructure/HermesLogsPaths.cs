using System.IO;

namespace Hermes.TradingPlatform.Shared.Infrastructure;

/// <summary>Unified log root for Hermes applications (override via HERMES_LOGS_ROOT).</summary>
public static class HermesLogsPaths
{
    public const string AppHermesWpf = "Hermes.Wpf";
    public const string AppTradingPlatform = "Hermes.TradingPlatform";
    public const string AppTradingCli = "Hermes.TradingPlatform.Cli";
    public const string WpfNoProjectFolder = "_session";

    private const string DefaultRoot = @"D:\Programming\AI_Agents\Hermes\Logs";

    public static string Root
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("HERMES_LOGS_ROOT")?.Trim();
            return string.IsNullOrEmpty(env) ? DefaultRoot : env;
        }
    }

    public static string GetAppDirectory(string appFolderName)
    {
        var dir = Path.Combine(Root, appFolderName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetWpfProjectDirectory(string? projectName)
    {
        var sub = SanitizeFolderName(projectName);
        if (string.IsNullOrEmpty(sub))
        {
            sub = WpfNoProjectFolder;
        }

        var dir = Path.Combine(GetAppDirectory(AppHermesWpf), sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string SanitizeFolderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim();
        return string.IsNullOrEmpty(safe) ? string.Empty : safe;
    }
}
