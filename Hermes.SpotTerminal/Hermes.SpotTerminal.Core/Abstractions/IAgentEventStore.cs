using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Core.Abstractions;

public interface IAgentEventStore
{
    string Location { get; }
    void Append(AgentEvent entry);
    IReadOnlyList<AgentEvent> LoadAll();
    void Clear();
}
