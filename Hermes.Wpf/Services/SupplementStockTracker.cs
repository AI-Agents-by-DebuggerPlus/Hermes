using Hermes.Wpf.Models.Biohacker;

namespace Hermes.Wpf.Services;

public sealed record StockAlert(string SupplementName, int DaysLeft, int ReorderThreshold)
{
    public bool IsCritical => DaysLeft <= ReorderThreshold / 2;
}

/// <summary>
/// Decrements stock_days_left for daily active supplements at most once per day and
/// surfaces low-stock alerts for the prompt context.
/// </summary>
public sealed class SupplementStockTracker
{
    private readonly BiohackerStateService _state;
    private readonly LogService _log;
    private readonly object _sync = new();
    private DateTime _lastCheckDateUtc = DateTime.MinValue;

    public SupplementStockTracker(BiohackerStateService state, LogService log)
    {
        _state = state;
        _log = log;
    }

    public async Task RunDailyCheckIfNeededAsync()
    {
        DateTime today = DateTime.UtcNow.Date;
        lock (_sync)
        {
            if (_lastCheckDateUtc.Date == today)
            {
                return;
            }

            _lastCheckDateUtc = today;
        }

        try
        {
            await DeductDailyDosesAsync().ConfigureAwait(false);
            var alerts = await GetAlertsAsync().ConfigureAwait(false);
            foreach (var alert in alerts)
            {
                _log.LogInfo($"[biohacker-stock] alert: {alert.SupplementName} {alert.DaysLeft}d left (threshold {alert.ReorderThreshold})");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarn($"[biohacker-stock] daily check failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<StockAlert>> GetAlertsAsync()
    {
        var low = await _state.GetLowStockAlertsAsync().ConfigureAwait(false);
        return low
            .Select(s => new StockAlert(s.Name, s.StockDaysLeft, s.ReorderThreshold))
            .ToList();
    }

    private async Task DeductDailyDosesAsync()
    {
        var stack = await _state.GetActiveStackAsync().ConfigureAwait(false);
        foreach (var card in stack)
        {
            if (!string.Equals(card.Frequency, "daily", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (card.StockDaysLeft <= 0)
            {
                continue;
            }

            await _state.UpdateStockAsync(card.Name).ConfigureAwait(false);
        }
    }
}
