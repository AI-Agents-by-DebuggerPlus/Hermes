namespace Hermes.TradingPlatform.Core.Domain;

public enum MarketFeedStatus
{
    Stopped,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

public enum MarketDataSource
{
    Mock,
    BinanceFutures,
}
