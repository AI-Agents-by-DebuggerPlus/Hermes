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
}
