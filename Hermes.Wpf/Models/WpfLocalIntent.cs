namespace Hermes.Wpf.Models;

/// <summary>CLI → WPF local action intent (skill=wpf_local).</summary>
public sealed class WpfLocalIntent
{
    public required string Action { get; init; }

    public string UserContext { get; init; } = string.Empty;

    public string ScheduleAction { get; init; } = string.Empty;

    public DateTime? RunAtLocal { get; init; }

    public int? WindowStartDay { get; init; }

    public int? WindowEndDay { get; init; }

    public int? Hour { get; init; }

    public int? Minute { get; init; }

    /// <summary>Agent scheduler: task title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Agent scheduler: command text to inject into chat when due.</summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>Agent scheduler: existing task id for complete/remove.</summary>
    public string TaskId { get; init; } = string.Empty;

    /// <summary>Agent scheduler: optional project override (default = current chat project).</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>Portfolio: category idea|in_dev|current|archive.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Portfolio: notes / description.</summary>
    public string Notes { get; init; } = string.Empty;
}
