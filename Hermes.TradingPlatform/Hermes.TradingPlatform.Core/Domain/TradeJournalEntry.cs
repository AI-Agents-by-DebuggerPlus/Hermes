namespace Hermes.TradingPlatform.Core.Domain;

/// <summary>One fill / balance-impacting trade line for the trading journal.</summary>
public sealed class TradeJournalEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    /// <summary>Open | Add | Reduce | Close</summary>
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
