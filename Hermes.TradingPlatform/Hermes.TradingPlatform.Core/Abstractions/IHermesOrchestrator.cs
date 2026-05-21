namespace Hermes.TradingPlatform.Core.Abstractions;

/// <summary>
/// Hermes orchestration: monitor, explain, review — never executes orders (Phase 6).
/// </summary>
public interface IHermesOrchestrator
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
