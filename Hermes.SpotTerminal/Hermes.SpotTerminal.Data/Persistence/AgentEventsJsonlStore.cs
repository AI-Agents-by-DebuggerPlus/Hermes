using System.Text.Json;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Shared.Bridge;

namespace Hermes.SpotTerminal.Data.Persistence;

public sealed class AgentEventsJsonlStore : IAgentEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly object _sync = new();

    public AgentEventsJsonlStore()
    {
        SpotBridgePaths.EnsureRoot();
    }

    public string Location => Path.Combine(SpotBridgePaths.DataRoot, "agent_events.jsonl");

    public void Append(AgentEvent entry)
    {
        lock (_sync)
        {
            File.AppendAllText(Location, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            File.WriteAllText(Location, string.Empty);
        }
    }

    public IReadOnlyList<AgentEvent> LoadAll()
    {
        lock (_sync)
        {
            if (!File.Exists(Location))
            {
                return [];
            }

            var list = new List<AgentEvent>();
            foreach (var line in File.ReadLines(Location))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var e = JsonSerializer.Deserialize<AgentEvent>(line, JsonOptions);
                    if (e is not null)
                    {
                        list.Add(e);
                    }
                }
                catch
                {
                    // skip bad line
                }
            }

            return list;
        }
    }
}
