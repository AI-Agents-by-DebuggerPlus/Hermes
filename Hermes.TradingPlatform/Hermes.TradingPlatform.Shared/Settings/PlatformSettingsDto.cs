namespace Hermes.TradingPlatform.Shared.Settings;

public sealed class PlatformSettingsDto
{
    public string MarketDataSource { get; set; } = "BinanceFutures";
    public bool HermesOrchestrationEnabled { get; set; } = true;
    public bool TradingSoundsEnabled { get; set; } = true;

    /// <summary>OpenRouter API key for in-app assistant.</summary>
    public string InAppAssistantOpenRouterApiKey { get; set; } = string.Empty;

    public string InAppAssistantOpenRouterModel { get; set; } = "openrouter/free";

    /// <summary>Starting balance when resetting paper account.</summary>
    public decimal InitialAccountBalance { get; set; } = 100_000m;

    /// <summary>Account leverage when mode is Fixed.</summary>
    public decimal AccountLeverage { get; set; } = 3m;

    /// <summary>Fixed or Maximum (uses risk max leverage).</summary>
    public string LeverageMode { get; set; } = "Fixed";
}
