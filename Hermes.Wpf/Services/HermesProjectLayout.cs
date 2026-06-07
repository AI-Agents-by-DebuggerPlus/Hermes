using System.IO;

namespace Hermes.Wpf.Services;

/// <summary>
/// On-disk layout for Hermes project artifacts (distinct from agent memory in <c>~/.hermes/</c>).
/// </summary>
public static class HermesProjectLayout
{
    public const string HermesFolderName = "hermes";
    public const string ProjectDataFileName = "project.md";
    public const string CredentialsFileName = "credentials.md";
    public const string ReadmeFileName = "README.md";
    public const string ScreenshotsFolderName = "screenshots";
    public const string RelativeScreenshotsPath = $"{HermesFolderName}/{ScreenshotsFolderName}";

    public static string GetHermesDirectory(string projectRoot) =>
        Path.Combine(projectRoot, HermesFolderName);

    public static string GetProjectDataPath(string projectRoot) =>
        Path.Combine(GetHermesDirectory(projectRoot), ProjectDataFileName);

    public static string GetScreenshotsDirectory(string projectRoot) =>
        Path.Combine(GetHermesDirectory(projectRoot), ScreenshotsFolderName);

    public static string GetScreenshotsWslPath(string wslWorkDir)
    {
        var root = (wslWorkDir ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrEmpty(root)
            ? string.Empty
            : $"{root}/{RelativeScreenshotsPath}";
    }
}
