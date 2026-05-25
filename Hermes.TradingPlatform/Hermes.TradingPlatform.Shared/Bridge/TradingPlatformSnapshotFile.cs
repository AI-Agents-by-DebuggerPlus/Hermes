namespace Hermes.TradingPlatform.Shared.Bridge;

public sealed class TradingPlatformSnapshotFile
{
    public DateTimeOffset TimestampUtc { get; init; }
    public bool TerminalRunning { get; init; }
    public string MarketDataSource { get; init; } = "BinanceFutures";
    public string FeedStatus { get; init; } = "";
    public AccountSnapshot Account { get; init; } = new();
    public PnlSnapshot Pnl { get; init; } = new();
    public RiskSnapshot Risk { get; init; } = new();
    public HermesSnapshot Hermes { get; init; } = new();
    public IReadOnlyList<PositionSnapshot> Positions { get; init; } = [];
    public IReadOnlyList<OrderSnapshot> Orders { get; init; } = [];
    public IReadOnlyList<StrategySnapshot> Strategies { get; init; } = [];
    public IReadOnlyList<MarketTickerSnapshot> Tickers { get; init; } = [];
    public IReadOnlyList<LogSnapshot> RecentLogs { get; init; } = [];
}

public sealed class AccountSnapshot
{
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal FreeMargin { get; init; }
    public decimal UsedMargin { get; init; }
    public decimal Leverage { get; init; }
}

public sealed class PnlSnapshot
{
    public decimal Today { get; init; }
    public decimal Week { get; init; }
    public decimal Month { get; init; }
    public decimal AllTime { get; init; }
}

public sealed class RiskSnapshot
{
    public string RiskLevel { get; init; } = "";
    public decimal DailyDrawdownPercent { get; init; }
    public decimal ExposurePercent { get; init; }
    public bool SafeMode { get; init; }
    public bool EmergencyHalt { get; init; }
    public decimal MaxLeverage { get; init; }
}

public sealed class HermesSnapshot
{
    public string State { get; init; } = "";
    public string ActiveStrategy { get; init; } = "";
    public decimal Confidence { get; init; }
    public string CurrentReasoning { get; init; } = "";
    public string StrategyContext { get; init; } = "";
}

public sealed class PositionSnapshot
{
    public string Symbol { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Size { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal UnrealizedPnl { get; init; }
}

public sealed class OrderSnapshot
{
    public string Id { get; init; } = "";
    public string Symbol { get; init; } = "";
    public string Type { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public string Status { get; init; } = "";
    public bool ReduceOnly { get; init; }
}

public sealed class StrategySnapshot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsEnabled { get; init; }
}

public sealed class MarketTickerSnapshot
{
    public string Symbol { get; init; } = "";
    public decimal Price { get; init; }
    public decimal ChangePercent24h { get; init; }
}

public sealed class LogSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string EventType { get; init; } = "";
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";
}
