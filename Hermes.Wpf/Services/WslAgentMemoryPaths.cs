using System.IO;
using System.Linq;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

internal static class WslAgentMemoryPaths
{
    internal static readonly string[] KnownMemoryFileNames = ["USER.md", "MEMORY.md"];

    internal static string? ResolveMemoriesDirectory(HermesSettings settings)
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

            string? best = null;
            foreach (var userDir in Directory.EnumerateDirectories(homeRoot))
            {
                var memories = Path.Combine(userDir, ".hermes", "memories");
                if (!Directory.Exists(memories))
                {
                    continue;
                }

                if (KnownMemoryFileNames.Any(name => File.Exists(Path.Combine(memories, name))))
                {
                    return memories;
                }

                best ??= memories;
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> SplitEntries(string raw)
    {
        var parts = raw.Split('§', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var text = part.Trim();
            if (text.EndsWith("---", StringComparison.Ordinal))
            {
                text = text[..^3].TrimEnd();
            }

            if (text.Length > 0)
            {
                list.Add(text);
            }
        }

        return list;
    }
}
