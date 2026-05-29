namespace Hermes.SpotTerminal.Shared.Bridge;

public static class SpotBridgePaths
{
    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesSpot");

    public static string BridgeRoot => Path.Combine(DataRoot, "bridge");

    public static string SnapshotFile => Path.Combine(BridgeRoot, "snapshot.json");
    public static string CommandsFile => Path.Combine(BridgeRoot, "commands.json");
    public static string HeartbeatFile => Path.Combine(BridgeRoot, "heartbeat.txt");

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(BridgeRoot);
    }
}
