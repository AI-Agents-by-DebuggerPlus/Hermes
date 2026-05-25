namespace Hermes.TradingPlatform.Core.Domain;

public sealed class RiskProfile
{
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public decimal MaxRiskPerTradePercent { get; set; } = 1m;
    public decimal MaxPositionSizeBtc { get; set; } = 0.5m;
    public decimal MaxLeverage { get; set; } = 5m;
    public decimal MaxExposurePercent { get; set; } = 50m;

    /// <summary>Take-profit distance = TP × stop-loss distance (RR ratio).</summary>
    public decimal DefaultTakeProfitRrMultiplier { get; set; } = 2m;

    /// <summary>When true and a position opens, auto-attach SL (Stop) and TP (Limit) reduce-only orders.</summary>
    public bool AutoApplyDefaultSlTp { get; set; } = true;

    public bool SafeMode { get; set; } = true;
    public bool AutoShutdown { get; set; } = true;
    public bool EmergencyHalt { get; set; }
    public decimal DailyDrawdownPercent { get; set; }
    public decimal ExposurePercent { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
}
