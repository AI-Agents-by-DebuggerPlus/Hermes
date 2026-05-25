using System.Text;
using System.Text.Json;
using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>Append-only backup of journal entries under %LocalAppData%/HermesTrading/trade_journal.jsonl</summary>
public sealed class TradeJournalFileWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
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

    public void Clear()
    {
        lock (Sync)
        {
            File.WriteAllText(FilePath, string.Empty, Utf8);
        }
    }

    public string FilePath { get; }

    public void Append(TradeJournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        lock (Sync)
        {
            File.AppendAllText(FilePath, line + Environment.NewLine, Utf8);
        }
    }
}
