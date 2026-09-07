namespace Hermes.Wpf.Models;

/// <summary>Parseable trade signal from Trading Analytics agent (<c>skill=trade_signal</c>).</summary>
public sealed class TradingAnalyticsSignalCard
{
    public required string SignalId { get; init; }
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public required double Entry { get; init; }
    public double? EntryHigh { get; init; }
    public double? StopLoss { get; init; }
    public double? TakeProfit { get; init; }
    public double? Lot { get; init; }
    public string? Timeframe { get; init; }
    public string? Summary { get; init; }
    public string? VisualizationPath { get; init; }

    public bool IsBuy => Side.StartsWith("buy", StringComparison.OrdinalIgnoreCase);

    public string PendingOrderTypeLabel => OrderType.ToLowerInvariant() switch
    {
        "stop" => IsBuy ? "Buy Stop" : "Sell Stop",
        "stop_limit" or "stoplimit" => IsBuy ? "Buy Stop Limit" : "Sell Stop Limit",
        _ => IsBuy ? "Buy Limit" : "Sell Limit",
    };

    public double EffectiveEntry =>
        EntryHigh is > 0 && EntryHigh.Value != Entry
            ? (Entry + EntryHigh.Value) / 2.0
            : Entry;
}
