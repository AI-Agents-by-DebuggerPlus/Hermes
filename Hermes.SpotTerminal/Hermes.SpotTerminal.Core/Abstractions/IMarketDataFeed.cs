namespace Hermes.SpotTerminal.Core.Abstractions;

public interface IMarketDataFeed : IDisposable
{
    string Name { get; }
    Task StartAsync(IReadOnlyList<string> symbols, CancellationToken ct = default);
    Task StopAsync();
}
