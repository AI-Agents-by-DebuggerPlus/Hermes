namespace Hermes.TradingPlatform.Core.Domain;

public sealed class Order
{
    public required string Id { get; init; }
    public required string Symbol { get; init; }
    public OrderType Type { get; set; }
    public OrderSide Side { get; set; }
    public decimal Price { get; set; }
    public decimal? TriggerPrice { get; set; }
    public decimal Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public bool ReduceOnly { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
