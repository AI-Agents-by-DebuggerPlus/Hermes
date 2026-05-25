namespace Hermes.TradingPlatform.Shared.Mock;

public sealed class AccountSummaryDto
{
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal FreeMargin { get; init; }
    public decimal UsedMargin { get; init; }
    public decimal Leverage { get; init; }
}

public sealed class PnlSummaryDto
{
    public decimal Today { get; init; }
    public decimal Week { get; init; }
    public decimal Month { get; init; }
    public decimal AllTime { get; init; }
}

public sealed class PositionDto
{
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public decimal Size { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal? LiquidationPrice { get; init; }
    public decimal? StopLossPrice { get; init; }
    public decimal? TakeProfitPrice { get; init; }
}

public sealed class OrderDto
{
    public required string Id { get; init; }
    public required string Symbol { get; init; }
    public required string Type { get; init; }
    public required string Side { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public required string Status { get; init; }
    public bool ReduceOnly { get; init; }

    /// <summary>"Entry", "SL" (reduce-only Stop), "TP" (reduce-only Limit), or "Reduce".</summary>
    public string Purpose { get; init; } = "Entry";
}

public sealed class RiskStatusDto
{
    public decimal DailyDrawdownPercent { get; init; }
    public decimal ExposurePercent { get; init; }
    public required string RiskLevel { get; init; }
    public decimal Leverage { get; init; }
}

public sealed class HermesStatusDto
{
    public required string State { get; init; }
    public required string ActiveStrategy { get; init; }
    public decimal Confidence { get; init; }
    public required string Mode { get; init; }
}

public sealed class StrategyCardDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskProfile { get; init; }
    public required string Status { get; init; }
    public bool IsEnabled { get; init; }
}

public sealed class MarketTickerDto
{
    public required string Symbol { get; init; }
    public decimal Price { get; init; }
    public decimal ChangePercent24h { get; init; }
    public decimal Volume24h { get; init; }
    public bool InWatchlist { get; init; }
}

public sealed class LogEntryDto
{
    public DateTime Timestamp { get; init; }
    public required string EventType { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}

public sealed class TradeJournalEntryDto
{
    public DateTime Timestamp { get; init; }
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    public required string Kind { get; init; }
    public required string Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal FillPrice { get; init; }
    public decimal Fee { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public bool ReduceOnly { get; init; }
}

public sealed class HermesTaskDto
{
    public required string Title { get; init; }
    public required string Status { get; init; }
}

public sealed class HermesDecisionDto
{
    public DateTime Timestamp { get; init; }
    public required string Summary { get; init; }
}
