using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.SpotTerminal.Data.Persistence;

public sealed class SpotSessionStateFileStore
{
    public string FilePath => Path.Combine(SpotBridgePaths.DataRoot, "session-state.json");

    public void Save(SpotPlatformState state)
    {
        SpotBridgePaths.EnsureRoot();
        var file = MapToFile(state);
        AtomicJsonFileStore.Save(FilePath, file);
    }

    public bool TryLoad(out SpotPlatformState state)
    {
        state = new SpotPlatformState();
        if (!AtomicJsonFileStore.TryLoad(FilePath, out SpotSessionStateFile? file) || file is null)
        {
            return false;
        }

        state = MapFromFile(file);
        return true;
    }

    private static SpotSessionStateFile MapToFile(SpotPlatformState s) => new()
    {
        Mode = s.Mode.ToString(),
        FeedStatus = s.FeedStatus,
        Agent = s.Agent,
        Balances = s.Balances,
        Orders = s.Orders,
        Tickers = s.Tickers,
        Skills = s.Skills,
        Logs = s.Logs.Take(200).ToList(),
        LearningJournal = s.LearningJournal.Take(500).ToList(),
    };

    private static SpotPlatformState MapFromFile(SpotSessionStateFile f) => new()
    {
        Mode = Enum.TryParse<ExecutionMode>(f.Mode, true, out var m) ? m : ExecutionMode.Virtual,
        FeedStatus = f.FeedStatus,
        Agent = f.Agent,
        Balances = f.Balances,
        Orders = f.Orders,
        Tickers = f.Tickers,
        Skills = f.Skills,
        Logs = f.Logs,
        LearningJournal = f.LearningJournal,
    };

    private sealed class SpotSessionStateFile
    {
        public string Mode { get; set; } = "Virtual";
        public string FeedStatus { get; set; } = "";
        public AgentSession Agent { get; set; } = new();
        public List<SpotBalance> Balances { get; set; } = [];
        public List<SpotOrder> Orders { get; set; } = [];
        public List<MarketTicker> Tickers { get; set; } = [];
        public List<Skill> Skills { get; set; } = [];
        public List<PlatformLogEntry> Logs { get; set; } = [];
        public List<LearningJournalEntry> LearningJournal { get; set; } = [];
    }
}
