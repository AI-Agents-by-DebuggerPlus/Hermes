using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Abstractions;

public interface ITradingStateStore
{
    TradingPlatformState Snapshot { get; }
    event EventHandler? StateChanged;
    void Initialize(TradingPlatformState state);
    void Mutate(Action<TradingPlatformState> update);
}
