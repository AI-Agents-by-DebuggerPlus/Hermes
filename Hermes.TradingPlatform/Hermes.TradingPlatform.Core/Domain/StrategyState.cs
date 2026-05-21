namespace Hermes.TradingPlatform.Core.Domain;

public sealed class StrategyState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskProfileLabel { get; init; }
    public StrategyRunStatus Status { get; set; }
    public bool IsEnabled { get; set; }
}
