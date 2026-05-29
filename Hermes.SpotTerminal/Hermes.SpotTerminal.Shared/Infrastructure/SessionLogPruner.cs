namespace Hermes.SpotTerminal.Shared.Infrastructure;

/// <summary>Deletes old session log files, keeping the newest N per directory.</summary>
public static class SessionLogPruner
{
    public const int DefaultKeepLatestSessions = 5;

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
                    // locked — skip
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }
}
