namespace Hermes.Wpf.Models;

/// <summary>In-memory state for the current role session (not persisted).</summary>
public sealed class RoleSession
{
    public AgentRole Role { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public int TurnCount { get; set; }
    public List<string> RecentSkillIds { get; } = [];
    public List<string> RecentTopics { get; } = [];

    public void RecordTurn(string? userMessage, string? skillId = null)
    {
        TurnCount++;
        if (!string.IsNullOrWhiteSpace(skillId))
        {
            RecentSkillIds.Remove(skillId);
            RecentSkillIds.Insert(0, skillId);
            while (RecentSkillIds.Count > 8)
            {
                RecentSkillIds.RemoveAt(RecentSkillIds.Count - 1);
            }
        }

        var topic = ExtractTopic(userMessage);
        if (topic.Length > 0)
        {
            RecentTopics.Remove(topic);
            RecentTopics.Insert(0, topic);
            while (RecentTopics.Count > 6)
            {
                RecentTopics.RemoveAt(RecentTopics.Count - 1);
            }
        }
    }

    private static string ExtractTopic(string? message)
    {
        var t = (message ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return string.Empty;
        }

        var line = t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? t;
        return line.Length > 80 ? line[..80] + "…" : line;
    }
}
