namespace Hermes.TradingPlatform.Core.Domain;

/// <summary>
/// User-editable runtime parameters for a strategy. Persisted to disk and
/// hot-reloaded by the runner. Every strategy reads the current parameters
/// snapshot on each evaluation, so changes apply on the next tick.
/// </summary>
public sealed class StrategyParameters
{
    public required string StrategyId { get; init; }

    /// <summary>Order size (base units, e.g. BTC quantity).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Threshold for triggering condition (e.g. 24h change percent).</summary>
    public decimal ChangeThresholdPercent { get; set; }

    /// <summary>Minimum time between signals (seconds).</summary>
    public int CooldownSeconds { get; set; }
}
