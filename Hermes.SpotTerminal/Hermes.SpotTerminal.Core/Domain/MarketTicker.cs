namespace Hermes.SpotTerminal.Core.Domain;

public sealed class MarketTicker
{
    public string Symbol { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Volume24h { get; set; }
    public bool InWatchlist { get; set; } = true;
}
