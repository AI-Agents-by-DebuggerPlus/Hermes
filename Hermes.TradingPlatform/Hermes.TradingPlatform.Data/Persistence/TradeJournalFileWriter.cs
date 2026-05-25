using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;

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
