using System.Text.Json;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Agent;

public sealed class AgentMonitoringService : IAgentMonitoringService
{
    private readonly IEventBus _bus;
    private readonly ISpotStateStore _store;

    public AgentMonitoringService(IEventBus bus, ISpotStateStore store)
    {
        _bus = bus;
        _store = store;
    }

    public void PublishThought(string summary, object? payload = null, string? symbol = null) =>
        Publish(AgentEventKind.Thought, summary, payload, symbol);

    public void PublishDecision(string summary, object? payload = null, string? symbol = null) =>
        Publish(AgentEventKind.Decision, summary, payload, symbol);

    public void PublishToolCall(string tool, object args, object? result = null, string? symbol = null) =>
        Publish(AgentEventKind.ToolCall, $"{tool}", new { tool, args, result }, symbol);

    public void PublishTradeExecuted(string summary, object payload, string? symbol = null) =>
        Publish(AgentEventKind.TradeExecuted, summary, payload, symbol);

    public void PublishStrategyStep(string strategyId, string step, object? metrics = null, string? symbol = null) =>
        Publish(AgentEventKind.StrategyStep, $"{strategyId}: {step}", new { strategyId, step, metrics }, symbol);

    private void Publish(AgentEventKind kind, string summary, object? payload, string? symbol)
    {
        var sessionId = _store.Snapshot.Agent.Id;
        var ev = new AgentEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = kind,
            SessionId = sessionId,
            Symbol = symbol,
            Summary = summary,
            PayloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload),
        };

        _bus.Publish(new AgentEventRecorded(ev));
    }
}
