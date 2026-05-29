using Hermes.SpotTerminal.Shared.Bridge;
using Hermes.TradingPlatform.Shared.Bridge;

namespace Hermes.Terminals.Shared.Bridge;

/// <summary>Unified bridge snapshot v2 — trading + spot + agent + skills.</summary>
public sealed class UnifiedTerminalSnapshotFile
{
    public int SchemaVersion { get; init; } = 2;
    public DateTimeOffset TimestampUtc { get; init; }
    public TradingPlatformSnapshotFile? TradingPlatform { get; init; }
    public SpotTerminalSnapshotSection? SpotTerminal { get; init; }
    public AgentSnapshotSection? Agent { get; init; }
    public SkillsSnapshotSection? Skills { get; init; }
}
