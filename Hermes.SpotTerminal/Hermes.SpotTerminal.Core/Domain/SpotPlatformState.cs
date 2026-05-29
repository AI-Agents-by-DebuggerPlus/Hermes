using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Core.Domain;

public sealed class SpotPlatformState
{
    public ExecutionMode Mode { get; set; } = ExecutionMode.Virtual;
    public string FeedStatus { get; set; } = "Disconnected";
    public AgentSession Agent { get; set; } = new();
    public List<SpotBalance> Balances { get; set; } = [];
    public List<SpotOrder> Orders { get; set; } = [];
    public List<MarketTicker> Tickers { get; set; } = [];
    public List<Skill> Skills { get; set; } = [];
    public List<PlatformLogEntry> Logs { get; set; } = [];
    public List<AgentEvent> AgentEvents { get; set; } = [];
    public List<LearningJournalEntry> LearningJournal { get; set; } = [];
}
