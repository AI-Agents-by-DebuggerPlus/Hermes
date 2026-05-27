// AUDIT 2026-05-25 (TradingExperienceExporter):
//   Bridge folder: %LocalAppData%/HermesTrading/bridge/
//     - snapshot.json   — TradingPlatformSnapshotFile DTO, full snapshot republished on every state change by TradingPlatformBridgePublisher
//     - commands.json   — queue of incoming TradingPlatformCommand entries from Hermes.Wpf / CLI
//     - heartbeat.txt   — ISO-8601 UTC timestamp; terminal is considered alive when delta < 12 s (see TradingPlatformBridgeService.IsTerminalAlive)
//     - result-{guid}.json — produced by CLI wait-result (separate, not enumerated here)
//   No additional files are created by the bridge layer.

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
