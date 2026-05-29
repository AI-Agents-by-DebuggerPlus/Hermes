using System.Text.Json;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.SpotTerminal.Data.Persistence;

public sealed class LearningJournalJsonlStore : ILearningJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _sync = new();

    public LearningJournalJsonlStore() => SpotBridgePaths.EnsureRoot();

    public string Location => Path.Combine(SpotBridgePaths.DataRoot, "learning_journal.jsonl");

    public void Append(LearningJournalEntry entry)
    {
        lock (_sync)
        {
            File.AppendAllText(Location, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
    }

    public IReadOnlyList<LearningJournalEntry> LoadAll()
    {
        lock (_sync)
        {
            if (!File.Exists(Location))
            {
                return [];
            }

            var list = new List<LearningJournalEntry>();
            foreach (var line in File.ReadLines(Location))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var e = JsonSerializer.Deserialize<LearningJournalEntry>(line, JsonOptions);
                    if (e is not null)
                    {
                        list.Add(e);
                    }
                }
                catch { /* skip */ }
            }

            return list;
        }
    }
}
