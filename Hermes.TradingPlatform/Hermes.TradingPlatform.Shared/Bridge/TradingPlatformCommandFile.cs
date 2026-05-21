namespace Hermes.TradingPlatform.Shared.Bridge;

public sealed class TradingPlatformCommandFile
{
    public IReadOnlyList<TradingPlatformCommand> Pending { get; init; } = [];
}

public sealed class TradingPlatformCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string? Symbol { get; set; }
    public string? Side { get; set; }
    public string? OrderType { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    public bool? ReduceOnly { get; set; }
    public string? OrderId { get; set; }
    public string? StrategyId { get; set; }
    public bool? Enabled { get; set; }
    public string? RequestedBy { get; set; }
}

public sealed class TradingPlatformCommandResultFile
{
    public Guid CommandId { get; init; }
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset CompletedUtc { get; init; } = DateTimeOffset.UtcNow;
}
