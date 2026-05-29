using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Data.Projections;

public sealed class AgentEventProjection
{
    private const int MaxInMemory = 500;

    public AgentEventProjection(ISpotStateStore store, IEventBus bus, IAgentEventStore storeDisk)
    {
        bus.Subscribe<AgentEventRecorded>(e =>
        {
            storeDisk.Append(e.Event);
            store.Mutate(s =>
            {
                s.AgentEvents.Insert(0, e.Event);
                if (s.AgentEvents.Count > MaxInMemory)
                {
                    s.AgentEvents.RemoveRange(MaxInMemory, s.AgentEvents.Count - MaxInMemory);
                }

                s.Agent.LastEventAtUtc = e.Event.TimestampUtc;
                if (e.Event.Kind == Core.Enums.AgentEventKind.Thought)
                {
                    s.Agent.CurrentThought = e.Event.Summary;
                }
            });
        });
    }
}
