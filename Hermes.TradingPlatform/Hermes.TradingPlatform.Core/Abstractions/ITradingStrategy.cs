using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Core.Abstractions;

public interface ITradingStrategy
{
    string Id { get; }
    string Name { get; }
    StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state);
}

/// <summary>Output of a strategy evaluation (Phase 5).</summary>
public sealed class StrategySignal
{
    public required string Symbol { get; init; }
    public OrderSide Side { get; init; }
    public OrderType OrderType { get; init; } = OrderType.Market;
    public decimal Quantity { get; init; }
    public decimal Price { get; init; }
    public required string Reason { get; init; }
    public bool AutoExecute { get; init; } = true;
}
