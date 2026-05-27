using System.Text;

namespace Hermes.Wpf.Models.Biohacker;

public sealed record ScheduleBlock(string Time, string Activity, string Category, string Supplement);

/// <summary>Stored at Health/Schedule/{schedule_type}.md.</summary>
public sealed class DailySchedule
{
    public string ScheduleType { get; set; } = "workday";
    public string Goal { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public List<ScheduleBlock> Blocks { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public string Issues { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string SourceFile { get; set; } = string.Empty;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: daily_schedule");
        sb.AppendLine("role: Biohacker");
        sb.AppendLine("tags: [schedule, productivity, biohacking]");
        sb.AppendLine("importance: 3");
        sb.Append("schedule_type: ").AppendLine(ScheduleType);
        sb.Append("status: ").AppendLine(Status);
        sb.Append("goal: ").Append('"').Append(BiohackerYaml.YamlString(Goal)).Append('"').AppendLine();
        sb.Append("last_updated: ").AppendLine(BiohackerYaml.IsoUtc(LastUpdated));
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# Распорядок: ").AppendLine(ScheduleType);
        sb.AppendLine();

        if (Blocks.Count > 0)
        {
            sb.AppendLine("## Блоки");
            foreach (var b in Blocks)
            {
                sb.Append("- ").Append(b.Time).Append(" — ").Append(b.Activity);
                if (!string.IsNullOrWhiteSpace(b.Category))
                {
                    sb.Append(" (").Append(b.Category).Append(')');
                }

                if (!string.IsNullOrWhiteSpace(b.Supplement))
                {
                    sb.Append(" · ").Append(b.Supplement);
                }

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        if (Rules.Count > 0)
        {
            sb.AppendLine("## Правила");
            foreach (var r in Rules)
            {
                sb.Append("- ").AppendLine(r);
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(Issues))
        {
            sb.AppendLine("## Проблемы");
            sb.AppendLine(Issues.Trim());
        }

        return sb.ToString();
    }

    public static DailySchedule? FromMemoryItem(MemoryItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.RawMarkdown))
        {
            return null;
        }

        var y = BiohackerYaml.ReadFrontmatter(item.RawMarkdown);
        var type = BiohackerYaml.Str(y, "type");
        if (type.Length > 0 && !string.Equals(type, "daily_schedule", StringComparison.OrdinalIgnoreCase))
        {
            if (!item.SourceFile.Replace('\\', '/').Contains("Health/Schedule/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return new DailySchedule
        {
            ScheduleType = string.IsNullOrWhiteSpace(BiohackerYaml.Str(y, "schedule_type"))
                ? System.IO.Path.GetFileNameWithoutExtension(item.SourceFile)
                : BiohackerYaml.Str(y, "schedule_type"),
            Goal = BiohackerYaml.Str(y, "goal"),
            Status = string.IsNullOrWhiteSpace(BiohackerYaml.Str(y, "status")) ? "active" : BiohackerYaml.Str(y, "status"),
            LastUpdated = BiohackerYaml.DateTimeUtc(y, "last_updated", DateTime.UtcNow),
            SourceFile = item.SourceFile,
        };
    }
}
