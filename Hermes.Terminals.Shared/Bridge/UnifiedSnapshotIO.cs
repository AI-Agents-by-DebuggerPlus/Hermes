using System.Text.Json;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Terminals.Shared.Bridge;

public static class UnifiedSnapshotIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static UnifiedTerminalSnapshotFile Read(string path)
    {
        if (!File.Exists(path))
        {
            return new UnifiedTerminalSnapshotFile { TimestampUtc = DateTimeOffset.UtcNow };
        }

        var json = File.ReadAllText(path);
        try
        {
            var unified = JsonSerializer.Deserialize<UnifiedTerminalSnapshotFile>(json, JsonOptions);
            if (unified is not null && unified.SchemaVersion >= 2)
            {
                return unified;
            }
        }
        catch
        {
            // legacy
        }

        try
        {
            var legacy = JsonSerializer.Deserialize<TradingPlatformSnapshotFile>(json, JsonOptions);
            if (legacy is not null)
            {
                return new UnifiedTerminalSnapshotFile
                {
                    SchemaVersion = 2,
                    TimestampUtc = legacy.TimestampUtc,
                    TradingPlatform = legacy,
                };
            }
        }
        catch
        {
            // ignore
        }

        return new UnifiedTerminalSnapshotFile { TimestampUtc = DateTimeOffset.UtcNow };
    }

    public static void WriteAtomic(string path, UnifiedTerminalSnapshotFile snapshot)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var toWrite = new UnifiedTerminalSnapshotFile
        {
            SchemaVersion = 2,
            TimestampUtc = DateTimeOffset.UtcNow,
            TradingPlatform = snapshot.TradingPlatform,
            SpotTerminal = snapshot.SpotTerminal,
            Agent = snapshot.Agent,
            Skills = snapshot.Skills,
        };
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(toWrite, JsonOptions));
        if (File.Exists(path))
        {
            File.Replace(temp, path, null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
