namespace Hermes.TradingPlatform.Core.Domain;

public sealed class RiskProfile
{
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public decimal MaxPositionSizeBtc { get; set; } = 0.5m;
    public decimal MaxLeverage { get; set; } = 5m;
    public decimal MaxExposurePercent { get; set; } = 50m;
    public bool SafeMode { get; set; } = true;
    public bool AutoShutdown { get; set; } = true;
    public bool EmergencyHalt { get; set; }
    public decimal DailyDrawdownPercent { get; set; }
    public decimal ExposurePercent { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
}
