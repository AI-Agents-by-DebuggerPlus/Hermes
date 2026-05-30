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

    /// <summary>True = LIMIT, false = MARKET. Legacy; use OrderEntryMode when set.</summary>
    public bool IsLimitOrder { get; set; } = true;

    /// <summary>Limit | Market | Conditional</summary>
    public string OrderEntryMode { get; set; } = "Limit";

    /// <summary>When conditional: limit or market after trigger.</summary>
    public bool ConditionalUseLimit { get; set; } = true;

    /// <summary>ContractPrice | MarkPrice</summary>
    public string StopWorkingType { get; set; } = "ContractPrice";

    public string OrderTimeInForce { get; set; } = "GTC";

    public bool OrderReduceOnly { get; set; }

    /// <summary>True = quantity in USDT, false = in contracts. Legacy field.</summary>
    public bool QuantityInUsdt { get; set; } = true;

    /// <summary>Contracts | UsdtOrderSize | UsdtInitialMargin</summary>
    public string QuantityInputMode { get; set; } = "UsdtOrderSize";

    /// <summary>When true, DefaultLeverage is applied to every symbol on switch and bulk-set on confirm.</summary>
    public bool ApplyDefaultLeverageToAllSymbols { get; set; }

    public int DefaultLeverage { get; set; } = 20;

    /// <summary>When true, DefaultMarginType is applied to every symbol on switch and bulk-set on confirm.</summary>
    public bool ApplyDefaultMarginTypeToAllSymbols { get; set; }

    /// <summary>Cross | Isolated</summary>
    public string DefaultMarginType { get; set; } = "Cross";

    /// <summary>Chart kline interval: 1m, 5m, 15m, 1h, 4h, 1d</summary>
    public string ChartInterval { get; set; } = "1m";
}
