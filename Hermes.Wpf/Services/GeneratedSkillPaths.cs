using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

internal static class GeneratedSkillPaths
{
    internal static string ResolveWindowsSkillsRoot(HermesSettings settings)
    {
        var configured = (settings.GeneratedSkillsDirectory ?? string.Empty).Trim();
        if (configured.Length > 0)
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesWpf",
            "skills");
    }

    internal static string? ResolveWslSkillsRoot(HermesSettings settings)
    {
        var distro = (settings.WslDistro ?? string.Empty).Trim();
        if (distro.Length == 0)
        {
            distro = "Ubuntu";
        }

        foreach (var hostRoot in new[] { @"\\wsl.localhost", @"\\wsl$" })
        {
            var homeRoot = Path.Combine(hostRoot, distro, "home");
            if (!Directory.Exists(homeRoot))
            {
                continue;
            }

            foreach (var userDir in Directory.EnumerateDirectories(homeRoot))
            {
                var skills = Path.Combine(userDir, ".hermes", "skills");
                var parent = Path.GetDirectoryName(skills);
                if (parent is not null && Directory.Exists(parent))
                {
                    Directory.CreateDirectory(skills);
                    return skills;
                }
            }
        }

        return null;
    }

    internal static string SkillFolder(string skillsRoot, string skillId) =>
        Path.Combine(skillsRoot, skillId);

    internal static string ManifestPath(string skillFolder) =>
        Path.Combine(skillFolder, "manifest.json");

    internal static string SkillMarkdownPath(string skillFolder) =>
        Path.Combine(skillFolder, "SKILL.md");

    internal static string IndexPath(string skillsRoot) =>
        Path.Combine(skillsRoot, "index.json");
}
