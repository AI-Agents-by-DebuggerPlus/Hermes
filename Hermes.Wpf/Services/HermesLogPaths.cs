using Hermes.TradingPlatform.Shared.Infrastructure;

namespace Hermes.Wpf.Services;

/// <summary>Wpf log paths under <see cref="HermesLogsPaths.Root"/> / Hermes.Wpf / {project}.</summary>
public static class HermesLogPaths
{
    public const string AppFolderName = HermesLogsPaths.WpfNoProjectFolder;

    public static string LogsRoot => HermesLogsPaths.Root;

    public static string GetProjectDirectory(string? projectName) =>
        HermesLogsPaths.GetWpfProjectDirectory(projectName);

    public static string SanitizeProjectFolderName(string? projectName)
    {
        var safe = HermesLogsPaths.SanitizeFolderName(projectName);
        return string.IsNullOrEmpty(safe) ? AppFolderName : safe;
    }
}
