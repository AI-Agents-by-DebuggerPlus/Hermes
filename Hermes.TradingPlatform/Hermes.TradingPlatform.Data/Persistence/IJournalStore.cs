using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>Append-only backup of <see cref="TradeJournalEntry"/> records.</summary>
public interface IJournalStore
{
    /// <summary>Provider-specific location: file path for JSON, DB path for SQLite, etc.</summary>
    string Location { get; }

    void Append(TradeJournalEntry entry);

    void Clear();

    IReadOnlyList<TradeJournalEntry> LoadAll();
}
