using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Data.Projections;

public sealed class EventLogProjection
{
    private const int MaxLogs = 200;

    public EventLogProjection(ISpotStateStore store, IEventBus bus)
    {
        bus.Subscribe<PlatformLogEvent>(e => Append(store, e.Entry));
        bus.Subscribe<AgentEventRecorded>(e =>
        {
            var entry = new PlatformLogEntry
            {
                Timestamp = e.Event.TimestampUtc,
                EventType = $"Agent{e.Event.Kind}",
                Source = "Agent",
                Message = e.Event.Summary,
            };
            Append(store, entry);
        });
    }

    private static void Append(ISpotStateStore store, PlatformLogEntry entry)
    {
        store.Mutate(s =>
        {
            s.Logs.Insert(0, entry);
            if (s.Logs.Count > MaxLogs)
            {
                s.Logs.RemoveRange(MaxLogs, s.Logs.Count - MaxLogs);
            }
        });
    }
}
