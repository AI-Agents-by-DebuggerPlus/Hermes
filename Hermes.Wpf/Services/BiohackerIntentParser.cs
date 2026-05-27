using System.Globalization;
using System.Text;
using System.Text.Json;
using Hermes.Wpf.Models.Biohacker;

namespace Hermes.Wpf.Services;

public abstract record BiohackerIntent;

public sealed record LogSupplementIntent(string Name, int DoseMg, string Timing, DateTime Date) : BiohackerIntent;

public sealed record UpdateSupplementIntent(SupplementCard Card) : BiohackerIntent;

public sealed record UpdateStockIntent(string Name, int DosesUsed) : BiohackerIntent;

public sealed record LogMetricsIntent(
    DateTime Date,
    int? SleepQuality,
    int? EnergyMorning,
    int? FocusDay,
    int? Mood,
    int? Productivity,
    int? Stress,
    string Notes) : BiohackerIntent;

public sealed record UpdateScheduleIntent(DailySchedule Schedule) : BiohackerIntent;

public sealed record SetGoalIntent(HealthGoal Goal) : BiohackerIntent;

public sealed record ScheduleChange(string TimeFrom, string TimeTo, string Block);

public sealed record OptimizeScheduleIntent(
    string ScheduleType,
    string Reason,
    IReadOnlyList<ScheduleChange> Changes) : BiohackerIntent;

/// <summary>
/// Locates {"bio":"..."} JSON objects in Hermes's reply, parses them into intents,
/// and returns the text with those JSON blocks removed so it can be shown to the user.
/// </summary>
public sealed class BiohackerIntentParser
{
    public (IReadOnlyList<BiohackerIntent> Intents, string CleanText) TryParseAll(string? rawResponse)
    {
        var text = rawResponse ?? string.Empty;
        var intents = new List<BiohackerIntent>();
        if (text.Length == 0)
        {
            return (intents, text);
        }

        var clean = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int braceStart = FindBioBraceStart(text, i);
            if (braceStart < 0)
            {
                clean.Append(text, i, text.Length - i);
                break;
            }

            clean.Append(text, i, braceStart - i);

            int braceEnd = FindMatchingBrace(text, braceStart);
            if (braceEnd < 0)
            {
                // Unbalanced — keep the remainder as-is.
                clean.Append(text, braceStart, text.Length - braceStart);
                break;
            }

            var json = text.Substring(braceStart, braceEnd - braceStart + 1);
            var intent = TryParseSingle(json);
            if (intent is not null)
            {
                intents.Add(intent);
            }
            else
            {
                // Leave malformed JSON visible to the user.
                clean.Append(json);
            }

            i = braceEnd + 1;
        }

        return (intents, CleanupWhitespace(clean.ToString()));
    }

    // -------- internals --------------------------------------------------------------

    private static int FindBioBraceStart(string text, int from)
    {
        int idx = from;
        while (idx < text.Length)
        {
            int brace = text.IndexOf('{', idx);
            if (brace < 0)
            {
                return -1;
            }

            int end = FindMatchingBrace(text, brace);
            if (end > brace)
            {
                var snippet = text.AsSpan(brace, end - brace + 1);
                if (snippet.Contains("\"bio\"", StringComparison.Ordinal))
                {
                    return brace;
                }

                idx = end + 1;
            }
            else
            {
                idx = brace + 1;
            }
        }

        return -1;
    }

    private static int FindMatchingBrace(string text, int openIdx)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = openIdx; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    if (depth < 0)
                    {
                        return -1;
                    }

                    break;
            }
        }

        return -1;
    }

    private static BiohackerIntent? TryParseSingle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("bio", out var bioProp))
            {
                return null;
            }

            var bio = bioProp.GetString() ?? string.Empty;
            return bio.ToLowerInvariant() switch
            {
                "log_supplement" => ParseLogSupplement(root),
                "update_supplement" => ParseUpdateSupplement(root),
                "update_stock" => ParseUpdateStock(root),
                "log_metrics" => ParseLogMetrics(root),
                "update_schedule" => ParseUpdateSchedule(root),
                "set_goal" => ParseSetGoal(root),
                "optimize_schedule" => ParseOptimizeSchedule(root),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static LogSupplementIntent ParseLogSupplement(JsonElement root) =>
        new(
            Name: Str(root, "name"),
            DoseMg: Int(root, "dose_mg"),
            Timing: Str(root, "timing"),
            Date: Date(root, "date", DateTime.UtcNow.Date));

    private static UpdateSupplementIntent ParseUpdateSupplement(JsonElement root)
    {
        var card = new SupplementCard
        {
            Name = Str(root, "name"),
            Category = Str(root, "category"),
            Status = string.IsNullOrWhiteSpace(Str(root, "status")) ? "active" : Str(root, "status"),
            DoseMg = Int(root, "dose_mg"),
            DoseUnit = string.IsNullOrWhiteSpace(Str(root, "dose_unit")) ? "mg" : Str(root, "dose_unit"),
            Timing = Str(root, "timing"),
            Frequency = string.IsNullOrWhiteSpace(Str(root, "frequency")) ? "daily" : Str(root, "frequency"),
            StockUnits = Int(root, "stock_units"),
            StockDaysLeft = Int(root, "stock_days_left"),
            ReorderThreshold = IntOr(root, "reorder_threshold", 14),
            ObservedEffects = StringList(root, "observed_effects"),
            StackCompatibility = Str(root, "stack_compatibility"),
            Notes = Str(root, "notes"),
            LastUpdated = DateTime.UtcNow,
        };
        return new UpdateSupplementIntent(card);
    }

    private static UpdateStockIntent ParseUpdateStock(JsonElement root) =>
        new(Name: Str(root, "name"), DosesUsed: IntOr(root, "doses_used", 1));

    private static LogMetricsIntent ParseLogMetrics(JsonElement root) =>
        new(
            Date: Date(root, "date", DateTime.UtcNow.Date),
            SleepQuality: IntOpt(root, "sleep_quality"),
            EnergyMorning: IntOpt(root, "energy_morning"),
            FocusDay: IntOpt(root, "focus_day"),
            Mood: IntOpt(root, "mood"),
            Productivity: IntOpt(root, "productivity"),
            Stress: IntOpt(root, "stress"),
            Notes: Str(root, "notes"));

    private static UpdateScheduleIntent ParseUpdateSchedule(JsonElement root)
    {
        var schedule = new DailySchedule
        {
            ScheduleType = string.IsNullOrWhiteSpace(Str(root, "schedule_type")) ? "workday" : Str(root, "schedule_type"),
            Goal = Str(root, "goal"),
            Status = string.IsNullOrWhiteSpace(Str(root, "status")) ? "active" : Str(root, "status"),
            Issues = Str(root, "issues"),
        };

        if (root.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in blocks.EnumerateArray())
            {
                schedule.Blocks.Add(new ScheduleBlock(
                    Time: Str(b, "time"),
                    Activity: Str(b, "activity"),
                    Category: Str(b, "category"),
                    Supplement: Str(b, "supplement")));
            }
        }

        schedule.Rules = StringList(root, "rules");
        return new UpdateScheduleIntent(schedule);
    }

    private static SetGoalIntent ParseSetGoal(JsonElement root)
    {
        var goal = new HealthGoal
        {
            GoalId = Str(root, "goal_id"),
            Title = Str(root, "title"),
            Priority = IntOr(root, "priority", 3),
            Status = string.IsNullOrWhiteSpace(Str(root, "status")) ? "active" : Str(root, "status"),
            TargetDate = DateOpt(root, "target_date"),
            SuccessMetrics = StringList(root, "success_metrics"),
            ActiveInterventions = StringList(root, "active_interventions"),
        };
        return new SetGoalIntent(goal);
    }

    private static OptimizeScheduleIntent ParseOptimizeSchedule(JsonElement root)
    {
        var changes = new List<ScheduleChange>();
        if (root.TryGetProperty("changes", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                changes.Add(new ScheduleChange(
                    TimeFrom: Str(c, "time_from"),
                    TimeTo: Str(c, "time_to"),
                    Block: Str(c, "block")));
            }
        }

        return new OptimizeScheduleIntent(
            ScheduleType: string.IsNullOrWhiteSpace(Str(root, "schedule_type")) ? "workday" : Str(root, "schedule_type"),
            Reason: Str(root, "reason"),
            Changes: changes);
    }

    // -------- json scalar helpers ---------------------------------------------------

    private static string Str(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
        {
            return string.Empty;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? string.Empty,
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty,
        };
    }

    private static int Int(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
        {
            return 0;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
        {
            return n;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
        {
            return m;
        }

        return 0;
    }

    private static int IntOr(JsonElement el, string prop, int defaultValue)
    {
        if (!el.TryGetProperty(prop, out var v))
        {
            return defaultValue;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
        {
            return n;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
        {
            return m;
        }

        return defaultValue;
    }

    private static int? IntOpt(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
        {
            return n;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
        {
            return m;
        }

        return null;
    }

    private static DateTime Date(JsonElement el, string prop, DateTime defaultValue)
    {
        var s = Str(el, prop);
        if (string.IsNullOrWhiteSpace(s))
        {
            return defaultValue;
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return defaultValue;
    }

    private static DateTime? DateOpt(JsonElement el, string prop)
    {
        var s = Str(el, prop);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }

        return null;
    }

    private static List<string> StringList(JsonElement el, string prop)
    {
        var result = new List<string>();
        if (!el.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    result.Add(s);
                }
            }
        }

        return result;
    }

    private static string CleanupWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var normalised = text.ReplaceLineEndings("\n");
        while (normalised.Contains("\n\n\n", StringComparison.Ordinal))
        {
            normalised = normalised.Replace("\n\n\n", "\n\n");
        }

        return normalised.TrimEnd() + (text.EndsWith('\n') ? "\n" : string.Empty);
    }
}
