namespace Hermes.Terminals.Shared.Bridge;

/// <summary>
/// Unified bridge lives alongside trading bridge (%LocalAppData%/HermesTrading/bridge).
/// Spot-only heartbeat uses HermesSpot/bridge when spot terminal runs alone.
/// </summary>
public static class UnifiedBridgePaths
{
    public static string TradingBridgeRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesTrading", "bridge");

    public static string UnifiedSnapshotFile => Path.Combine(TradingBridgeRoot, "snapshot.json");
    public static string UnifiedCommandsFile => Path.Combine(TradingBridgeRoot, "commands.json");
    public static string UnifiedHeartbeatFile => Path.Combine(TradingBridgeRoot, "heartbeat.txt");

    public static void EnsureTradingBridgeRoot() => Directory.CreateDirectory(TradingBridgeRoot);
}
