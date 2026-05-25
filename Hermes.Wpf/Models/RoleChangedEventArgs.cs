namespace Hermes.Wpf.Models;

public sealed class RoleChangedEventArgs(AgentRole previous, AgentRole current) : EventArgs
{
    public AgentRole Previous { get; } = previous;
    public AgentRole Current { get; } = current;
}
