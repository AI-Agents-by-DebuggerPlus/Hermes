namespace Hermes.BinanceDemoFuturesTerminal.Models;

public sealed class PlatformSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    public bool RiskManagementEnabled { get; set; } = true;

    /// <summary>Max notional per single order (USDT).</summary>
    public double MaxOrderUsdt { get; set; } = 500;

    /// <summary>Max total open position notional across symbols (USDT).</summary>
    public double MaxTotalExposureUsdt { get; set; } = 2000;

    /// <summary>Max simultaneous open positions.</summary>
    public int MaxOpenPositions { get; set; } = 5;

    /// <summary>Max leverage allowed for new exposure checks (informational).</summary>
    public int MaxLeverage { get; set; } = 20;

    /// <summary>True = LIMIT, false = MARKET.</summary>
    public bool IsLimitOrder { get; set; } = true;

    /// <summary>True = quantity in USDT, false = in contracts.</summary>
    public bool QuantityInUsdt { get; set; } = true;
}
