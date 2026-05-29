namespace Hermes.SpotTerminal.Core.Abstractions;

public interface IAgentMonitoringService
{
    void PublishThought(string summary, object? payload = null, string? symbol = null);
    void PublishDecision(string summary, object? payload = null, string? symbol = null);
    void PublishToolCall(string tool, object args, object? result = null, string? symbol = null);
    void PublishTradeExecuted(string summary, object payload, string? symbol = null);
    void PublishStrategyStep(string strategyId, string step, object? metrics = null, string? symbol = null);
}
