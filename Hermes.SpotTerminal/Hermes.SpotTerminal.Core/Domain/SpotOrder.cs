using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Core.Domain;

public sealed class SpotOrder
{
    public string Id { get; set; } = "";
    public string Symbol { get; set; } = "";
    public SpotOrderType Type { get; set; }
    public SpotOrderSide Side { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public SpotOrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
