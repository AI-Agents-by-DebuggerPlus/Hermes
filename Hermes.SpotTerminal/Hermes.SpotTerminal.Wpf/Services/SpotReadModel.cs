using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Wpf.Services;

public sealed class SpotReadModel
{
    private readonly ISpotStateStore _store;

    public SpotReadModel(ISpotStateStore store)
    {
        _store = store;
        _store.StateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<SpotBalance> GetBalances() => _store.Snapshot.Balances;
    public IReadOnlyList<SpotOrder> GetOrders() => _store.Snapshot.Orders;
    public IReadOnlyList<MarketTicker> GetTickers() => _store.Snapshot.Tickers;
    public IReadOnlyList<PlatformLogEntry> GetLogs(string? sourceFilter = null)
    {
        var logs = _store.Snapshot.Logs;
        return sourceFilter is null
            ? logs
            : logs.Where(l => string.Equals(l.Source, sourceFilter, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public IReadOnlyList<Skill> GetSkills() => _store.Snapshot.Skills;
    public AgentSession GetAgent() => _store.Snapshot.Agent;
    public IReadOnlyList<LearningJournalEntry> GetJournal() => _store.Snapshot.LearningJournal;
}
