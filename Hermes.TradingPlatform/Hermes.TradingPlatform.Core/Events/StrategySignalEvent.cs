using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Events;

public sealed record StrategySignalEvent(
    string StrategyId,
    string StrategyName,
    string Symbol,
    OrderSide Side,
    OrderType OrderType,
    decimal Quantity,
    decimal Price,
    string Reason,
    bool AutoExecuteRequested) : PlatformEventBase
{
    public override string EventType => "StrategySignal";
}
