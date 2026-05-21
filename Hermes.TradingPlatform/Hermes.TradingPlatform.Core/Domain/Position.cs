namespace Hermes.TradingPlatform.Core.Domain;

public sealed class Position
{
    public required string Symbol { get; init; }
    public PositionSide Side { get; set; }
    public decimal Size { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal? LiquidationPrice { get; set; }
}
