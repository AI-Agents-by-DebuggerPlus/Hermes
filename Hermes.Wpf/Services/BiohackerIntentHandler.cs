using Hermes.Wpf.Models.Biohacker;

namespace Hermes.Wpf.Services;

/// <summary>
/// Applies <see cref="BiohackerIntent"/> objects produced by <see cref="BiohackerIntentParser"/>
/// to disk via <see cref="BiohackerStateService"/> and refreshes the External Brain index.
/// </summary>
public sealed class BiohackerIntentHandler
{
    private readonly BiohackerStateService _state;
    private readonly SupplementStockTracker _stockTracker;
    private readonly ExternalBrainService _brain;
    private readonly LogService _log;

    public BiohackerIntentHandler(
        BiohackerStateService state,
        SupplementStockTracker stockTracker,
        ExternalBrainService brain,
        LogService log)
    {
        _state = state;
        _stockTracker = stockTracker;
        _brain = brain;
        _log = log;
    }

    public async Task HandleAsync(BiohackerIntent intent)
    {
        if (intent is null)
        {
            return;
        }

        try
        {
            switch (intent)
            {
                case LogSupplementIntent i:
                    await HandleLogSupplement(i).ConfigureAwait(false);
                    break;
                case UpdateSupplementIntent i:
                    await HandleUpdateSupplement(i).ConfigureAwait(false);
                    break;
                case UpdateStockIntent i:
                    await HandleUpdateStock(i).ConfigureAwait(false);
                    break;
                case LogMetricsIntent i:
                    await HandleLogMetrics(i).ConfigureAwait(false);
                    break;
                case UpdateScheduleIntent i:
                    await HandleUpdateSchedule(i).ConfigureAwait(false);
                    break;
                case OptimizeScheduleIntent i:
                    await HandleOptimizeSchedule(i).ConfigureAwait(false);
                    break;
                case SetGoalIntent i:
                    await HandleSetGoal(i).ConfigureAwait(false);
                    break;
            }

            _brain.RestartWatcherAndReload("biohacker-intent");
            _log.LogInfo($"[biohacker-intent] Handled {intent.GetType().Name}");
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[biohacker-intent] failed {intent.GetType().Name}: {ex.Message}");
        }
    }

    private async Task HandleLogSupplement(LogSupplementIntent i)
    {
        if (string.IsNullOrWhiteSpace(i.Name))
        {
            return;
        }

        var today = await _state.GetOrCreateTodayLogAsync().ConfigureAwait(false);
        var existing = today.SupplementsTaken.FirstOrDefault(s =>
            string.Equals(s.Name, i.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Timing, i.Timing, StringComparison.OrdinalIgnoreCase));
        if (existing == default)
        {
            today.SupplementsTaken.Add(new SupplementTaken(i.Name, i.DoseMg, i.Timing, true));
        }

        await _state.SaveDailyLogAsync(today).ConfigureAwait(false);
        await _state.UpdateStockAsync(i.Name).ConfigureAwait(false);
    }

    private async Task HandleUpdateSupplement(UpdateSupplementIntent i)
    {
        if (string.IsNullOrWhiteSpace(i.Card.Name))
        {
            return;
        }

        var supplements = await _state.GetAllSupplementsAsync().ConfigureAwait(false);
        var existing = supplements.FirstOrDefault(s =>
            string.Equals(s.Name, i.Card.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Category = OrDefault(i.Card.Category, existing.Category);
            existing.Status = OrDefault(i.Card.Status, existing.Status);
            existing.DoseMg = i.Card.DoseMg > 0 ? i.Card.DoseMg : existing.DoseMg;
            existing.DoseUnit = OrDefault(i.Card.DoseUnit, existing.DoseUnit);
            existing.Timing = OrDefault(i.Card.Timing, existing.Timing);
            existing.Frequency = OrDefault(i.Card.Frequency, existing.Frequency);
            existing.StockUnits = i.Card.StockUnits > 0 ? i.Card.StockUnits : existing.StockUnits;
            existing.StockDaysLeft = i.Card.StockDaysLeft > 0 ? i.Card.StockDaysLeft : existing.StockDaysLeft;
            existing.ReorderThreshold = i.Card.ReorderThreshold > 0 ? i.Card.ReorderThreshold : existing.ReorderThreshold;
            if (i.Card.ObservedEffects.Count > 0)
            {
                foreach (var e in i.Card.ObservedEffects)
                {
                    if (!existing.ObservedEffects.Contains(e, StringComparer.OrdinalIgnoreCase))
                    {
                        existing.ObservedEffects.Add(e);
                    }
                }
            }

            existing.StackCompatibility = OrDefault(i.Card.StackCompatibility, existing.StackCompatibility);
            existing.Notes = OrDefault(i.Card.Notes, existing.Notes);
            await _state.SaveSupplementCardAsync(existing).ConfigureAwait(false);
        }
        else
        {
            await _state.SaveSupplementCardAsync(i.Card).ConfigureAwait(false);
        }
    }

    private async Task HandleUpdateStock(UpdateStockIntent i)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.DosesUsed <= 0)
        {
            return;
        }

        await _state.UpdateStockAsync(i.Name, i.DosesUsed).ConfigureAwait(false);
    }

    private async Task HandleLogMetrics(LogMetricsIntent i)
    {
        var today = await _state.GetOrCreateTodayLogAsync().ConfigureAwait(false);
        today.Date = i.Date == default ? today.Date : i.Date.Date;
        today.SleepQuality = i.SleepQuality ?? today.SleepQuality;
        today.EnergyMorning = i.EnergyMorning ?? today.EnergyMorning;
        today.FocusDay = i.FocusDay ?? today.FocusDay;
        today.Mood = i.Mood ?? today.Mood;
        today.Productivity = i.Productivity ?? today.Productivity;
        today.Stress = i.Stress ?? today.Stress;
        if (!string.IsNullOrWhiteSpace(i.Notes))
        {
            today.Notes = string.IsNullOrWhiteSpace(today.Notes)
                ? i.Notes
                : today.Notes + "\n" + i.Notes;
        }

        await _state.SaveDailyLogAsync(today).ConfigureAwait(false);
    }

    private async Task HandleUpdateSchedule(UpdateScheduleIntent i)
    {
        await _state.SaveScheduleAsync(i.Schedule).ConfigureAwait(false);
    }

    private async Task HandleOptimizeSchedule(OptimizeScheduleIntent i)
    {
        var schedule = await _state.GetActiveScheduleAsync(DateTime.Now.DayOfWeek).ConfigureAwait(false)
            ?? new DailySchedule { ScheduleType = i.ScheduleType };

        foreach (var change in i.Changes)
        {
            var block = schedule.Blocks.FirstOrDefault(b =>
                string.Equals(b.Time, change.TimeFrom, StringComparison.Ordinal));
            if (block is null)
            {
                schedule.Blocks.Add(new ScheduleBlock(change.TimeTo, change.Block, string.Empty, string.Empty));
            }
            else
            {
                var idx = schedule.Blocks.IndexOf(block);
                schedule.Blocks[idx] = block with { Time = change.TimeTo, Activity = change.Block };
            }
        }

        schedule.Issues = string.IsNullOrWhiteSpace(i.Reason) ? schedule.Issues : i.Reason;
        await _state.SaveScheduleAsync(schedule).ConfigureAwait(false);
    }

    private async Task HandleSetGoal(SetGoalIntent i)
    {
        if (string.IsNullOrWhiteSpace(i.Goal.GoalId) && string.IsNullOrWhiteSpace(i.Goal.Title))
        {
            return;
        }

        await _state.SaveGoalAsync(i.Goal).ConfigureAwait(false);
    }

    private static string OrDefault(string newValue, string fallback) =>
        string.IsNullOrWhiteSpace(newValue) ? fallback : newValue;
}
