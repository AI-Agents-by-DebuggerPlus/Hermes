namespace Hermes.TradingPlatform.Shared.Infrastructure;

/// <summary>Deletes old session log files, keeping the newest N sessions per directory.</summary>
public static class SessionLogPruner
{
    /// <summary>Keep current session plus this many previous sessions (2 = delete from 3rd oldest onward).</summary>
    public const int DefaultKeepLatestSessions = 2;

    public static int PruneDirectory(string directory, string filePattern, int keepLatest = DefaultKeepLatestSessions)
    {
        if (keepLatest < 1 || !Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            var logs = new DirectoryInfo(directory)
                .GetFiles(filePattern, SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deleted = 0;
            foreach (var old in logs.Skip(keepLatest))
            {
                try
                {
                    old.Delete();
                    deleted++;
                }
                catch
                {
                    // file locked — skip
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    public static int PruneAppTree(string appDirectory, IEnumerable<string> filePatterns, int keepLatest = DefaultKeepLatestSessions)
    {
        if (!Directory.Exists(appDirectory))
        {
            return 0;
        }

        var total = 0;
        foreach (var pattern in filePatterns)
        {
            total += PruneDirectory(appDirectory, pattern, keepLatest);
            foreach (var sub in Directory.EnumerateDirectories(appDirectory))
            {
                total += PruneDirectory(sub, pattern, keepLatest);
            }
        }

        return total;
    }
}
