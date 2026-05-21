namespace Hermes.TradingPlatform.Core.Domain;

public sealed class MarketTicker
{
    public required string Symbol { get; init; }
    public decimal Price { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Volume24h { get; set; }
    public bool InWatchlist { get; set; }
}
