namespace Hermes.TradingPlatform.Core.Domain;

public sealed class HermesState
{
    public HermesOrchestrationState State { get; set; } = HermesOrchestrationState.Monitoring;
    public string ActiveStrategy { get; set; } = "";
    public decimal Confidence { get; set; }
    public string Mode { get; set; } = "Orchestration / Paper";
    public string CurrentReasoning { get; set; } = "";
    public string StrategyContext { get; set; } = "";
    public List<HermesTask> Tasks { get; } = [];
    public List<HermesDecision> Decisions { get; } = [];
}

public sealed class HermesTask
{
    public required string Title { get; init; }
    public required string Status { get; init; }
}

public sealed class HermesDecision
{
    public DateTimeOffset Timestamp { get; init; }
    public required string Summary { get; init; }
}
