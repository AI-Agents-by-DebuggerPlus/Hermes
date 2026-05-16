using System.IO;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class WslAgentMemoryService
{
    public string? ResolveMemoriesDirectory(HermesSettings settings) =>
        WslAgentMemoryPaths.ResolveMemoriesDirectory(settings);

    public IReadOnlyList<WslMemoryFileSnapshot> LoadSnapshots(HermesSettings settings)
    {
        var dir = WslAgentMemoryPaths.ResolveMemoriesDirectory(settings);
        if (dir is null)
        {
            return [];
        }

        var list = new List<WslMemoryFileSnapshot>();
        foreach (var name in WslAgentMemoryPaths.KnownMemoryFileNames)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var info = new FileInfo(path);
            var raw = File.ReadAllText(path).ReplaceLineEndings("\n").Trim();
            list.Add(new WslMemoryFileSnapshot
            {
                FileName = name,
                FullPath = path,
                LastWriteTimeLocal = info.LastWriteTime,
                RawContent = raw,
                Entries = WslAgentMemoryPaths.SplitEntries(raw),
            });
        }

        return list;
    }
}
