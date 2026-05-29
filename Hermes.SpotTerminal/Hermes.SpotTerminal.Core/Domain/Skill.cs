using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Core.Domain;

public sealed class Skill
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public SkillStatus Status { get; set; } = SkillStatus.Draft;
    public bool IsInitial { get; set; }
    public string? ParametersJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public BacktestSummary? LastBacktest { get; set; }
}

public sealed class BacktestSummary
{
    public DateTimeOffset RunAtUtc { get; set; }
    public int Trades { get; set; }
    public decimal NetPnl { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public bool PassedThreshold { get; set; }
}
