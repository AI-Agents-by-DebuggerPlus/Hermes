using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;

// AUDIT 2026-05-25 (TradingExperienceExporter):
//   Source of truth for per-fill economic events (Open/Add/Reduce/Close + Fee + RealizedPnl + Balance trajectory).
//   File: %LocalAppData%/HermesTrading/trade_journal.jsonl (append-only, line-delimited JSON).
//   Lifecycle:
//     - Append called on every fill from VirtualExchangeEngine.FillOrder (via TradeJournalProjection).
//     - LoadAll used by ReplayViewModel / JournalViewModel.
//     - Clear called only when paper account is reset (Account settings page).
//   Bridge exposure: NONE. trade_journal.jsonl is NOT mirrored into snapshot.json.
//   Hermes.Wpf cannot subscribe to journal entries directly; it only sees aggregate Pnl / Balance / Positions in the bridge snapshot.
//   For External Brain ingestion, an exporter must either (a) poll this file or (b) diff snapshot.Pnl / snapshot.Account between updates.

namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>Append-only backup of journal entries under %LocalAppData%/HermesTrading/trade_journal.jsonl.</summary>
public sealed class TradeJournalFileWriter : IJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly object Sync = new();

    public TradeJournalFileWriter(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HermesTrading");
        Directory.CreateDirectory(dir);
        FilePath = filePath ?? Path.Combine(dir, "trade_journal.jsonl");
    }

    public string FilePath { get; }

    public string Location => FilePath;

    public void Clear()
    {
        lock (Sync)
        {
            File.WriteAllText(FilePath, string.Empty, Utf8);
        }
    }

    public void Append(TradeJournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        lock (Sync)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine, Utf8);
        }
    }

    public IReadOnlyList<TradeJournalEntry> LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var entries = new List<TradeJournalEntry>();
        lock (Sync)
        {
            foreach (var line in File.ReadLines(FilePath, Utf8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<TradeJournalEntry>(line, JsonOptions);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // skip malformed line, keep going
                }
            }
        }

        return entries;
    }
}
