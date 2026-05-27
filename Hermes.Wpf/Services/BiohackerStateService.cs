using System.Globalization;
using System.IO;
using System.Text;
using Hermes.Wpf.Models;
using Hermes.Wpf.Models.Biohacker;

namespace Hermes.Wpf.Services;

/// <summary>
/// Reads/writes Biohacker domain objects living in {vault}/Health/*. Hot-path methods
/// (BuildContextSnapshotAsync, GetActiveStackAsync) avoid disk I/O — they parse from
/// the ExternalBrainService cache instead.
/// </summary>
public sealed class BiohackerStateService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ExternalBrainService _brain;
    private readonly HermesSettings _settings;
    private readonly LogService _log;

    public BiohackerStateService(ExternalBrainService brain, HermesSettings settings, LogService log)
    {
        _brain = brain;
        _settings = settings;
        _log = log;
    }

    // ----- Supplements --------------------------------------------------------------

    public async Task<IReadOnlyList<SupplementCard>> GetAllSupplementsAsync()
    {
        var all = await _brain.GetAllMemoriesAsync().ConfigureAwait(false);
        return all
            .Where(m => NormalizedPath(m.SourceFile).Contains("Health/Supplements/", StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(Path.GetFileNameWithoutExtension(m.SourceFile), "README", StringComparison.OrdinalIgnoreCase))
            .Select(SupplementCard.FromMemoryItem)
            .Where(c => c is not null)
            .Cast<SupplementCard>()
            .ToList();
    }

    public async Task<IReadOnlyList<SupplementCard>> GetActiveStackAsync()
    {
        var all = await GetAllSupplementsAsync().ConfigureAwait(false);
        return all
            .Where(c => string.Equals(c.Status, "active", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => TimingOrder(c.Timing))
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task SaveSupplementCardAsync(SupplementCard card)
    {
        var vault = _brain.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            return Task.CompletedTask;
        }

        var dir = Path.Combine(vault, "Health", "Supplements");
        Directory.CreateDirectory(dir);
        var fileName = string.IsNullOrWhiteSpace(card.SourceFile)
            ? Path.Combine(dir, Sanitize(card.Name) + ".md")
            : card.SourceFile;
        card.SourceFile = fileName;
        card.LastUpdated = DateTime.UtcNow;
        File.WriteAllText(fileName, card.ToMarkdown(), Utf8NoBom);
        _log.LogInfo($"[biohacker] saved supplement card: {Path.GetFileName(fileName)}");
        return Task.CompletedTask;
    }

    public async Task UpdateStockAsync(string supplementName, int dosesUsed = 1)
    {
        if (string.IsNullOrWhiteSpace(supplementName) || dosesUsed <= 0)
        {
            return;
        }

        var supplements = await GetAllSupplementsAsync().ConfigureAwait(false);
        var card = supplements.FirstOrDefault(s =>
            string.Equals(s.Name, supplementName, StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            _log.LogWarn($"[biohacker] update_stock: supplement '{supplementName}' not found");
            return;
        }

        var dailyDose = card.DoseMg > 0 ? 1 : 1;
        card.StockDaysLeft = Math.Max(0, card.StockDaysLeft - dosesUsed * dailyDose);
        card.StockUnits = Math.Max(0, card.StockUnits - dosesUsed);
        if (card.StockUnits == 0 && card.StockDaysLeft == 0)
        {
            card.Status = "out_of_stock";
        }

        await SaveSupplementCardAsync(card).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SupplementCard>> GetLowStockAlertsAsync()
    {
        var supplements = await GetAllSupplementsAsync().ConfigureAwait(false);
        return supplements
            .Where(s => string.Equals(s.Status, "active", StringComparison.OrdinalIgnoreCase))
            .Where(s => s.StockDaysLeft > 0 && s.StockDaysLeft <= s.ReorderThreshold)
            .OrderBy(s => s.StockDaysLeft)
            .ToList();
    }

    // ----- Daily log ----------------------------------------------------------------

    public async Task<DailyHealthLog> GetOrCreateTodayLogAsync()
    {
        var today = DateTime.UtcNow.Date;
        var logs = await GetRecentLogsAsync(2).ConfigureAwait(false);
        var existing = logs.FirstOrDefault(l => l.Date.Date == today);
        if (existing is not null)
        {
            return existing;
        }

        return new DailyHealthLog { Date = today };
    }

    public Task SaveDailyLogAsync(DailyHealthLog log)
    {
        var vault = _brain.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            return Task.CompletedTask;
        }

        var dir = Path.Combine(vault, "Health", "Journal");
        Directory.CreateDirectory(dir);
        var fileName = Path.Combine(dir, log.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");
        log.SourceFile = fileName;
        File.WriteAllText(fileName, log.ToMarkdown(), Utf8NoBom);
        _log.LogInfo($"[biohacker] saved daily log: {Path.GetFileName(fileName)}");
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DailyHealthLog>> GetRecentLogsAsync(int days = 14)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);
        var all = await _brain.GetAllMemoriesAsync().ConfigureAwait(false);
        return all
            .Where(m => NormalizedPath(m.SourceFile).Contains("Health/Journal/", StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(Path.GetFileNameWithoutExtension(m.SourceFile), "README", StringComparison.OrdinalIgnoreCase))
            .Select(DailyHealthLog.FromMemoryItem)
            .Where(l => l is not null && l.Date >= cutoff)
            .Cast<DailyHealthLog>()
            .OrderByDescending(l => l.Date)
            .ToList();
    }

    // ----- Schedule -----------------------------------------------------------------

    public async Task<DailySchedule?> GetActiveScheduleAsync(DayOfWeek day)
    {
        var all = await _brain.GetAllMemoriesAsync().ConfigureAwait(false);
        var schedules = all
            .Where(m => NormalizedPath(m.SourceFile).Contains("Health/Schedule/", StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(Path.GetFileNameWithoutExtension(m.SourceFile), "README", StringComparison.OrdinalIgnoreCase))
            .Select(DailySchedule.FromMemoryItem)
            .Where(s => s is not null)
            .Cast<DailySchedule>()
            .ToList();

        var wanted = day is DayOfWeek.Saturday or DayOfWeek.Sunday ? "weekend" : "workday";
        return schedules.FirstOrDefault(s => string.Equals(s.ScheduleType, wanted, StringComparison.OrdinalIgnoreCase))
            ?? schedules.FirstOrDefault();
    }

    public Task SaveScheduleAsync(DailySchedule schedule)
    {
        var vault = _brain.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            return Task.CompletedTask;
        }

        var dir = Path.Combine(vault, "Health", "Schedule");
        Directory.CreateDirectory(dir);
        var fileName = string.IsNullOrWhiteSpace(schedule.SourceFile)
            ? Path.Combine(dir, Sanitize(schedule.ScheduleType) + ".md")
            : schedule.SourceFile;
        schedule.SourceFile = fileName;
        schedule.LastUpdated = DateTime.UtcNow;
        File.WriteAllText(fileName, schedule.ToMarkdown(), Utf8NoBom);
        _log.LogInfo($"[biohacker] saved schedule: {Path.GetFileName(fileName)}");
        return Task.CompletedTask;
    }

    // ----- Goals --------------------------------------------------------------------

    public async Task<IReadOnlyList<HealthGoal>> GetActiveGoalsAsync()
    {
        var all = await _brain.GetAllMemoriesAsync().ConfigureAwait(false);
        return all
            .Where(m => NormalizedPath(m.SourceFile).Contains("Health/Goals/", StringComparison.OrdinalIgnoreCase))
            .Where(m => !string.Equals(Path.GetFileNameWithoutExtension(m.SourceFile), "README", StringComparison.OrdinalIgnoreCase))
            .Select(HealthGoal.FromMemoryItem)
            .Where(g => g is not null && string.Equals(g.Status, "active", StringComparison.OrdinalIgnoreCase))
            .Cast<HealthGoal>()
            .OrderBy(g => g.Priority)
            .ToList();
    }

    public Task SaveGoalAsync(HealthGoal goal)
    {
        var vault = _brain.ResolveEffectiveMemoryPath();
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            return Task.CompletedTask;
        }

        var dir = Path.Combine(vault, "Health", "Goals");
        Directory.CreateDirectory(dir);
        var fileName = string.IsNullOrWhiteSpace(goal.SourceFile)
            ? Path.Combine(dir, Sanitize(string.IsNullOrWhiteSpace(goal.GoalId) ? goal.Title : goal.GoalId) + ".md")
            : goal.SourceFile;
        goal.SourceFile = fileName;
        File.WriteAllText(fileName, goal.ToMarkdown(), Utf8NoBom);
        _log.LogInfo($"[biohacker] saved goal: {Path.GetFileName(fileName)}");
        return Task.CompletedTask;
    }

    // ----- Metrics ------------------------------------------------------------------

    public async Task<HealthMetricsSummary> ComputeMetricsSummaryAsync(int days = 7)
    {
        var logs = await GetRecentLogsAsync(days).ConfigureAwait(false);
        var n = logs.Count;
        if (n == 0)
        {
            return new HealthMetricsSummary(0, 0, 0, 0, 0, 0, 0, "stable");
        }

        double Avg(Func<DailyHealthLog, int?> sel)
        {
            var vals = logs.Select(sel).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            return vals.Count == 0 ? 0 : Math.Round(vals.Average(), 2);
        }

        var trend = ComputeTrend(logs);
        return new HealthMetricsSummary(
            AvgSleepQuality: Avg(l => l.SleepQuality),
            AvgEnergyMorning: Avg(l => l.EnergyMorning),
            AvgFocus: Avg(l => l.FocusDay),
            AvgMood: Avg(l => l.Mood),
            AvgProductivity: Avg(l => l.Productivity),
            AvgStress: Avg(l => l.Stress),
            DaysAnalyzed: n,
            Trend: trend);
    }

    // ----- Prompt block --------------------------------------------------------------

    public async Task<string> BuildContextSnapshotAsync()
    {
        var sb = new StringBuilder();

        var stack = await GetActiveStackAsync().ConfigureAwait(false);
        var lowStock = await GetLowStockAlertsAsync().ConfigureAwait(false);
        sb.AppendLine("[Активный стек — сегодня]");
        if (stack.Count == 0)
        {
            sb.AppendLine("• нет активных карточек БАДов");
        }
        else
        {
            foreach (var s in stack)
            {
                sb.Append("• ").Append(s.Name);
                if (s.DoseMg > 0)
                {
                    sb.Append(' ').Append(s.DoseMg.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(s.DoseUnit);
                }

                if (!string.IsNullOrWhiteSpace(s.Timing))
                {
                    sb.Append(" — ").Append(TimingDescription(s.Timing));
                }

                sb.AppendLine();
            }
        }

        foreach (var alert in lowStock)
        {
            sb.Append("⚠ ").Append(alert.Name)
                .Append(": осталось ").Append(alert.StockDaysLeft.ToString(CultureInfo.InvariantCulture))
                .AppendLine(" дней — пора заказать");
        }

        var metrics = await ComputeMetricsSummaryAsync(7).ConfigureAwait(false);
        sb.AppendLine();
        sb.AppendLine("[Метрики (7 дней avg)]");
        if (metrics.DaysAnalyzed == 0)
        {
            sb.AppendLine("Данных нет — дневник пуст за последнюю неделю.");
        }
        else
        {
            sb.Append("Сон: ").Append(metrics.AvgSleepQuality.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | Энергия: ").Append(metrics.AvgEnergyMorning.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | Фокус: ").Append(metrics.AvgFocus.ToString("F1", CultureInfo.InvariantCulture))
                .Append(" | Тренд: ").AppendLine(metrics.Trend);
        }

        var goals = await GetActiveGoalsAsync().ConfigureAwait(false);
        sb.AppendLine();
        sb.AppendLine("[Активные цели]");
        if (goals.Count == 0)
        {
            sb.AppendLine("• целей не задано");
        }
        else
        {
            var i = 1;
            foreach (var g in goals)
            {
                sb.Append(i++).Append(". ").Append(string.IsNullOrWhiteSpace(g.Title) ? g.GoalId : g.Title)
                    .Append(" (приоритет ").Append(g.Priority.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            }
        }

        var schedule = await GetActiveScheduleAsync(DateTime.Now.DayOfWeek).ConfigureAwait(false);
        if (schedule is not null)
        {
            sb.AppendLine();
            sb.Append("[Распорядок — ").Append(ScheduleDisplay(schedule.ScheduleType)).AppendLine("]");
            if (schedule.Blocks.Count == 0 && !string.IsNullOrWhiteSpace(schedule.Goal))
            {
                sb.AppendLine(schedule.Goal);
            }
            else
            {
                foreach (var b in schedule.Blocks.Take(4))
                {
                    sb.Append(b.Time).Append(" — ").AppendLine(b.Activity);
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ----- Helpers -----------------------------------------------------------------

    private static string NormalizedPath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/');

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "item-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || (ch >= 'А' && ch <= 'я') || ch == 'ё' || ch == 'Ё')
            {
                sb.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append('_');
            }
        }

        return sb.Length == 0
            ? "item-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
            : sb.ToString();
    }

    private static int TimingOrder(string timing) =>
        timing?.ToLowerInvariant() switch
        {
            "morning" => 1,
            "fasted" => 2,
            "with_meal" => 3,
            "afternoon" => 4,
            "evening" => 5,
            "before_sleep" => 6,
            _ => 7,
        };

    private static string TimingDescription(string timing) =>
        timing?.ToLowerInvariant() switch
        {
            "morning" => "утром",
            "afternoon" => "днём",
            "evening" => "вечером",
            "before_sleep" => "перед сном",
            "with_meal" => "с едой",
            "fasted" => "натощак",
            _ => timing ?? string.Empty,
        };

    private static string ScheduleDisplay(string scheduleType) =>
        scheduleType?.ToLowerInvariant() switch
        {
            "workday" => "Рабочий день",
            "weekend" => "Выходной",
            _ => string.IsNullOrWhiteSpace(scheduleType) ? "—" : scheduleType,
        };

    private static string ComputeTrend(IReadOnlyList<DailyHealthLog> logs)
    {
        if (logs.Count < 4)
        {
            return "stable";
        }

        var ordered = logs.OrderBy(l => l.Date).ToList();
        var half = ordered.Count / 2;
        var first = ordered.Take(half).ToList();
        var second = ordered.Skip(ordered.Count - half).ToList();

        double Avg(IEnumerable<DailyHealthLog> ls, Func<DailyHealthLog, int?> sel)
        {
            var vals = ls.Select(sel).Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            return vals.Count == 0 ? 0 : vals.Average();
        }

        var energyDelta = Avg(second, l => l.EnergyMorning) - Avg(first, l => l.EnergyMorning);
        var focusDelta = Avg(second, l => l.FocusDay) - Avg(first, l => l.FocusDay);
        var combined = energyDelta + focusDelta;
        if (combined > 0.5)
        {
            return "improving";
        }

        if (combined < -0.5)
        {
            return "declining";
        }

        return "stable";
    }
}
