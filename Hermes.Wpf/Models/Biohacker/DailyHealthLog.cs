using System.Globalization;
using System.Text;

namespace Hermes.Wpf.Models.Biohacker;

public readonly record struct SupplementTaken(string Name, int DoseMg, string Timing, bool Taken);

/// <summary>One day of subjective health metrics, persisted to Health/Journal/{yyyy-MM-dd}.md.</summary>
public sealed class DailyHealthLog
{
    public DateTime Date { get; set; }

    /// <summary>Minutes from midnight (e.g. 06:30 = 390).</summary>
    public int? WakeTimeMinutes { get; set; }

    public int? SleepQuality { get; set; }
    public int? EnergyMorning { get; set; }
    public int? Mood { get; set; }
    public int? FocusDay { get; set; }
    public int? Productivity { get; set; }
    public int? Stress { get; set; }
    public int? PhysicalWellbeing { get; set; }

    public List<SupplementTaken> SupplementsTaken { get; set; } = new();
    public string PhysicalActivity { get; set; } = string.Empty;
    public string Nutrition { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: daily_health_log");
        sb.AppendLine("role: Biohacker");
        sb.AppendLine("tags: [health, journal, daily]");
        sb.AppendLine("importance: 3");
        sb.Append("date: ").AppendLine(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AppendOptInt(sb, "wake_time_minutes", WakeTimeMinutes);
        AppendOptInt(sb, "sleep_quality", SleepQuality);
        AppendOptInt(sb, "energy_morning", EnergyMorning);
        AppendOptInt(sb, "mood", Mood);
        AppendOptInt(sb, "focus_day", FocusDay);
        AppendOptInt(sb, "productivity", Productivity);
        AppendOptInt(sb, "stress", Stress);
        AppendOptInt(sb, "physical_wellbeing", PhysicalWellbeing);
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append("# Day ").AppendLine(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.AppendLine();

        if (SupplementsTaken.Count > 0)
        {
            sb.AppendLine("## БАДы");
            foreach (var s in SupplementsTaken)
            {
                var status = s.Taken ? "✓" : "—";
                sb.Append("- ").Append(status).Append(' ')
                    .Append(s.Name).Append(' ').Append(s.DoseMg.ToString(CultureInfo.InvariantCulture)).Append(" mg");
                if (!string.IsNullOrWhiteSpace(s.Timing))
                {
                    sb.Append(" (").Append(s.Timing).Append(')');
                }

                sb.AppendLine();
            }

            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(PhysicalActivity))
        {
            sb.AppendLine("## Активность");
            sb.AppendLine(PhysicalActivity.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(Nutrition))
        {
            sb.AppendLine("## Питание");
            sb.AppendLine(Nutrition.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(Notes))
        {
            sb.AppendLine("## Заметки");
            sb.AppendLine(Notes.Trim());
        }

        return sb.ToString();
    }

    public static DailyHealthLog? FromMemoryItem(MemoryItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.RawMarkdown))
        {
            return null;
        }

        var y = BiohackerYaml.ReadFrontmatter(item.RawMarkdown);
        var type = BiohackerYaml.Str(y, "type");
        if (type.Length > 0 && !string.Equals(type, "daily_health_log", StringComparison.OrdinalIgnoreCase))
        {
            if (!item.SourceFile.Replace('\\', '/').Contains("Health/Journal/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var date = BiohackerYaml.DateTimeUtcOpt(y, "date");
        if (date is null)
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(item.SourceFile);
            if (DateTime.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
            {
                date = parsedDate;
            }
        }

        return new DailyHealthLog
        {
            Date = date?.Date ?? DateTime.UtcNow.Date,
            WakeTimeMinutes = BiohackerYaml.IntOpt(y, "wake_time_minutes"),
            SleepQuality = BiohackerYaml.IntOpt(y, "sleep_quality"),
            EnergyMorning = BiohackerYaml.IntOpt(y, "energy_morning"),
            Mood = BiohackerYaml.IntOpt(y, "mood"),
            FocusDay = BiohackerYaml.IntOpt(y, "focus_day"),
            Productivity = BiohackerYaml.IntOpt(y, "productivity"),
            Stress = BiohackerYaml.IntOpt(y, "stress"),
            PhysicalWellbeing = BiohackerYaml.IntOpt(y, "physical_wellbeing"),
            Notes = item.Content?.Trim() ?? string.Empty,
            SourceFile = item.SourceFile,
        };
    }

    private static void AppendOptInt(StringBuilder sb, string key, int? v)
    {
        if (v.HasValue)
        {
            sb.Append(key).Append(": ").Append(v.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }
    }
}
