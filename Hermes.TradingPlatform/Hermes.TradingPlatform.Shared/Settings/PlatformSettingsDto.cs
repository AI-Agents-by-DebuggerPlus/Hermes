namespace Hermes.TradingPlatform.Shared.Settings;

public sealed class PlatformSettingsDto
{
    public string MarketDataSource { get; init; } = "Mock";
    public bool HermesOrchestrationEnabled { get; init; } = true;
    public bool TradingSoundsEnabled { get; init; } = true;
}
