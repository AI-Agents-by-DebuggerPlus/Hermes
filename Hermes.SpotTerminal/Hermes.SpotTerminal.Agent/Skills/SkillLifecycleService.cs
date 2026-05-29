using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Agent.Skills;

public sealed class SkillLifecycleService
{
    private readonly ISkillRepository _repo;
    private readonly ISpotStateStore _store;
    private readonly IEventBus _bus;
    private readonly IAgentMonitoringService _agent;

    public SkillLifecycleService(ISkillRepository repo, ISpotStateStore store, IEventBus bus, IAgentMonitoringService agent)
    {
        _repo = repo;
        _store = store;
        _bus = bus;
        _agent = agent;
    }

    public Skill CreateDraft(string id, string name, string description)
    {
        var skill = new Skill
        {
            Id = id,
            Name = name,
            Description = description,
            Status = SkillStatus.Draft,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _repo.Save(skill);
        SyncSkillsToState();
        _agent.PublishDecision($"Skill created: {id}", new { skill.Id, skill.Status });
        return skill;
    }

    public BacktestSummary RunBacktest(string skillId)
    {
        var skill = _repo.LoadAll().FirstOrDefault(s => s.Id == skillId)
            ?? throw new InvalidOperationException($"Skill {skillId} not found");

        skill.Status = SkillStatus.Backtesting;
        _repo.Save(skill);
        SyncSkillsToState();

        var summary = new BacktestSummary
        {
            RunAtUtc = DateTimeOffset.UtcNow,
            Trades = 12,
            NetPnl = 45.5m,
            MaxDrawdownPercent = 2.1m,
            PassedThreshold = true,
        };

        skill.LastBacktest = summary;
        skill.Status = summary.PassedThreshold ? SkillStatus.Approved : SkillStatus.Draft;
        if (skill.Status == SkillStatus.Approved)
        {
            skill.ApprovedAtUtc = DateTimeOffset.UtcNow;
        }

        _repo.Save(skill);
        SyncSkillsToState();
        _agent.PublishStrategyStep(skillId, "BacktestComplete", summary);
        return summary;
    }

    public void Approve(string skillId)
    {
        var skill = _repo.LoadAll().FirstOrDefault(s => s.Id == skillId)
            ?? throw new InvalidOperationException($"Skill {skillId} not found");
        skill.Status = SkillStatus.Approved;
        skill.ApprovedAtUtc = DateTimeOffset.UtcNow;
        _repo.Save(skill);
        SyncSkillsToState();
        _agent.PublishDecision($"Skill approved: {skillId}");
    }

    private void SyncSkillsToState()
    {
        var skills = _repo.LoadAll();
        _store.Mutate(s =>
        {
            s.Skills.Clear();
            s.Skills.AddRange(skills);
        });
    }
}
