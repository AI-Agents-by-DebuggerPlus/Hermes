namespace Hermes.TradingPlatform.Shared.Risk;

/// <summary>Editable risk limits (UI + file persistence).</summary>
public sealed class RiskProfileSettingsDto
{
    public decimal MaxDailyLossPercent { get; init; }
    public decimal MaxPositionSizeBtc { get; init; }
    public decimal MaxLeverage { get; init; }
    public decimal MaxExposurePercent { get; init; }
    public bool SafeMode { get; init; }
    public bool AutoShutdown { get; init; }
    public bool EmergencyHalt { get; init; }
}
