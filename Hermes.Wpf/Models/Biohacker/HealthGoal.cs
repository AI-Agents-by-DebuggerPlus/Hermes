using System.Globalization;
using System.Text;

namespace Hermes.Wpf.Models.Biohacker;

/// <summary>Stored at Health/Goals/{goal_id}.md.</summary>
public sealed class HealthGoal
{
    public string GoalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Priority { get; set; } = 3;
    public string Status { get; set; } = "active";
    public DateTime? TargetDate { get; set; }
    public List<string> SuccessMetrics { get; set; } = new();
    public List<string> ActiveInterventions { get; set; } = new();
    public string SourceFile { get; set; } = string.Empty;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: health_goal");
        sb.AppendLine("role: Biohacker");
        sb.AppendLine("tags: [goal, health, biohacking]");
        sb.AppendLine("importance: 4");
        sb.Append("goal_id: ").AppendLine(GoalId);
        sb.Append("title: ").Append('"').Append(BiohackerYaml.YamlString(Title)).Append('"').AppendLine();
        sb.Append("priority: ").Append(Priority.ToString(CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("status: ").AppendLine(Status);
        if (TargetDate.HasValue)
        {
            sb.Append("target_date: ").AppendLine(TargetDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# Цель: ").AppendLine(string.IsNullOrWhiteSpace(Title) ? GoalId : Title);
        sb.AppendLine();

        if (SuccessMetrics.Count > 0)
        {
            sb.AppendLine("## Метрики успеха");
            foreach (var m in SuccessMetrics)
            {
                sb.Append("- ").AppendLine(m);
            }

            sb.AppendLine();
        }

        if (ActiveInterventions.Count > 0)
        {
            sb.AppendLine("## Активные вмешательства");
            foreach (var i in ActiveInterventions)
            {
                sb.Append("- ").AppendLine(i);
            }
        }

        return sb.ToString();
    }

    public static HealthGoal? FromMemoryItem(MemoryItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.RawMarkdown))
        {
            return null;
        }

        var y = BiohackerYaml.ReadFrontmatter(item.RawMarkdown);
        var type = BiohackerYaml.Str(y, "type");
        if (type.Length > 0 && !string.Equals(type, "health_goal", StringComparison.OrdinalIgnoreCase))
        {
            if (!item.SourceFile.Replace('\\', '/').Contains("Health/Goals/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var goalId = BiohackerYaml.Str(y, "goal_id");
        if (goalId.Length == 0)
        {
            goalId = System.IO.Path.GetFileNameWithoutExtension(item.SourceFile);
            if (string.Equals(goalId, "README", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return new HealthGoal
        {
            GoalId = goalId,
            Title = BiohackerYaml.Str(y, "title"),
            Priority = BiohackerYaml.Int(y, "priority", 3),
            Status = string.IsNullOrWhiteSpace(BiohackerYaml.Str(y, "status")) ? "active" : BiohackerYaml.Str(y, "status"),
            TargetDate = BiohackerYaml.DateTimeUtcOpt(y, "target_date"),
            SourceFile = item.SourceFile,
        };
    }
}
