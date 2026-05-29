using Hermes.SpotTerminal.Core.Domain;

namespace Hermes.SpotTerminal.Core.Abstractions;

public interface ISpotStateStore
{
    SpotPlatformState Snapshot { get; }
    event EventHandler? StateChanged;
    void Initialize(SpotPlatformState state);
    void Mutate(Action<SpotPlatformState> update);
}
