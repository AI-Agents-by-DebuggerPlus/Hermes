using System.Text.Json.Serialization;

namespace Hermes.Wpf.Models;

public enum AgentTaskStatus
{
    Scheduled = 0,
    Fired = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// Reminder-only scheduled task: Hermes.Wpf stores it and at DueAt sends <see cref="Command"/>
/// to the owning project agent. WPF does not execute the work itself.
/// </summary>
public sealed class AgentScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    public string Title { get; set; } = string.Empty;

    /// <summary>Text injected into the project chat as if the user asked the agent.</summary>
    public string Command { get; set; } = string.Empty;

    public string ProjectName { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = "agent";

    public DateTime DueAtLocal { get; set; }

    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Scheduled;

    public DateTime CreatedAtLocal { get; set; } = DateTime.Now;

    public DateTime? FiredAtLocal { get; set; }

    public DateTime? CompletedAtLocal { get; set; }

    public string? Notes { get; set; }

    [JsonIgnore]
    public bool IsActive => Status is AgentTaskStatus.Scheduled or AgentTaskStatus.Fired;

    [JsonIgnore]
    public bool IsOverdue =>
        Status == AgentTaskStatus.Scheduled && DueAtLocal <= DateTime.Now;
}
