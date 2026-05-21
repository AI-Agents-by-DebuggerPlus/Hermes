namespace Hermes.TradingPlatform.Data.Persistence;

/// <summary>Serializable paper-trading session snapshot (%LocalAppData%/HermesTrading/session-state.json).</summary>
public sealed class TradingSessionStateFile
{
    public int Version { get; set; } = 1;
    public DateTimeOffset SavedAtUtc { get; set; }
    public int NextOrderSequence { get; set; } = 1004;
    public AccountFileModel Account { get; set; } = new();
    public PnlFileModel Pnl { get; set; } = new();
    public List<PositionFileModel> Positions { get; set; } = [];
    public List<OrderFileModel> Orders { get; set; } = [];
    public List<TickerFileModel> Tickers { get; set; } = [];
    public List<StrategyFileModel> Strategies { get; set; } = [];
    public List<JournalFileModel> Journal { get; set; } = [];
    public List<LogFileModel> Logs { get; set; } = [];
}

public sealed class AccountFileModel
{
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal FreeMargin { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal Leverage { get; set; } = 1m;
}

public sealed class PnlFileModel
{
    public decimal Today { get; set; }
    public decimal Week { get; set; }
    public decimal Month { get; set; }
    public decimal AllTime { get; set; }
}

public sealed class PositionFileModel
{
    public required string Symbol { get; set; }
    public required string Side { get; set; }
    public decimal Size { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal? LiquidationPrice { get; set; }
}

public sealed class OrderFileModel
{
    public required string Id { get; set; }
    public required string Symbol { get; set; }
    public required string Type { get; set; }
    public required string Side { get; set; }
    public decimal Price { get; set; }
    public decimal? TriggerPrice { get; set; }
    public decimal Quantity { get; set; }
    public required string Status { get; set; }
    public bool ReduceOnly { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TickerFileModel
{
    public required string Symbol { get; set; }
    public decimal Price { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Volume24h { get; set; }
    public bool InWatchlist { get; set; }
}

public sealed class StrategyFileModel
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string RiskProfileLabel { get; set; }
    public required string Status { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class JournalFileModel
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string OrderId { get; set; }
    public required string Symbol { get; set; }
    public required string Kind { get; set; }
    public required string Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal FillPrice { get; set; }
    public decimal Fee { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public bool ReduceOnly { get; set; }
}

public sealed class LogFileModel
{
    public DateTimeOffset Timestamp { get; set; }
    public required string EventType { get; set; }
    public required string Source { get; set; }
    public required string Message { get; set; }
}
