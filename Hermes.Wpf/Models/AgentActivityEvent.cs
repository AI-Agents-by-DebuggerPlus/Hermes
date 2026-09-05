namespace Hermes.Wpf.Models;

public sealed class AgentActivityEvent
{
    public DateTime AtLocal { get; init; } = DateTime.Now;

    public string Workspace { get; init; } = "(none)";

    public string Kind { get; init; } = "info";

    public string Message { get; init; } = string.Empty;

    public bool IsError { get; init; }

    public string DisplayLine =>
        $"{AtLocal:HH:mm:ss} [{Workspace}] {Kind}: {Message}";
}
