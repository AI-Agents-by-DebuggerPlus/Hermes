using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Events;

/// <summary>Aggregated trade tick (Binance @aggTrade or equivalent).</summary>
public sealed record MarketTradeEvent(
    string Symbol,
    decimal Price,
    decimal Quantity,
    OrderSide AggressorSide,
    long TradeId,
    DateTimeOffset TradeTime) : PlatformEventBase
{
    public override string EventType => "MarketTrade";
}

/// <summary>Kline / candlestick update (open/high/low/close + interval).</summary>
public sealed record MarketKlineEvent(
    string Symbol,
    string Interval,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal QuoteVolume,
    int TradeCount,
    bool IsClosed) : PlatformEventBase
{
    public override string EventType => "MarketKline";
}
