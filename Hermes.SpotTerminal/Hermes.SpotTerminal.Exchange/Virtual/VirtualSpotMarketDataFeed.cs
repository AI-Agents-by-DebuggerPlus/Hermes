using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Exchange.Virtual;

public sealed class VirtualSpotMarketDataFeed : IMarketDataFeed
{
    private readonly IEventBus _bus;
    private readonly ISpotStateStore _store;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public VirtualSpotMarketDataFeed(IEventBus bus, ISpotStateStore store)
    {
        _bus = bus;
        _store = store;
    }

    public string Name => "Virtual Spot";

    public Task StartAsync(IReadOnlyList<string> symbols, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _store.Mutate(s => s.FeedStatus = "Connected (Virtual)");
        _loop = Task.Run(() => RunLoopAsync(symbols, _cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }

        _store.Mutate(s => s.FeedStatus = "Disconnected");
    }

    private async Task RunLoopAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var rnd = new Random(42);
        var prices = symbols.ToDictionary(s => s, s =>
            _store.Snapshot.Tickers.FirstOrDefault(t => t.Symbol == s)?.Price ?? 100m);

        while (!ct.IsCancellationRequested)
        {
            foreach (var sym in symbols)
            {
                var delta = (decimal)(rnd.NextDouble() * 0.002 - 0.001);
                prices[sym] = Math.Max(0.01m, prices[sym] * (1 + delta));
                _bus.Publish(new MarketTickEvent(sym, prices[sym], 0m, 0m));
            }

            await Task.Delay(800, ct);
        }
    }

    public void Dispose() => _cts?.Cancel();
}
