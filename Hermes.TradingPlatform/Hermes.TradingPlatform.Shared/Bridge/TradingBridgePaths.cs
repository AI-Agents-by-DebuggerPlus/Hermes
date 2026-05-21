namespace Hermes.TradingPlatform.Shared.Bridge;

public static class TradingBridgePaths
{
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesTrading", "bridge");

    public static string SnapshotFile => Path.Combine(RootDirectory, "snapshot.json");
    public static string CommandsFile => Path.Combine(RootDirectory, "commands.json");
    public static string HeartbeatFile => Path.Combine(RootDirectory, "heartbeat.txt");

    public static void EnsureRoot() => Directory.CreateDirectory(RootDirectory);
}
