using Hermes.TradingPlatform.Core.Abstractions;
using Hermes.TradingPlatform.Core.Domain;
using Hermes.TradingPlatform.Core.Events;

namespace Hermes.TradingPlatform.Exchange.MarketData;

public sealed class MockMarketDataFeed : IMarketDataFeed
{
    private readonly IEventBus _bus;
    private readonly Dictionary<string, decimal> _prices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new(42);
    private Timer? _timer;

    public MockMarketDataFeed(IEventBus bus, IEnumerable<(string Symbol, decimal Price)> seedPrices)
    {
        _bus = bus;
        foreach (var (symbol, price) in seedPrices)
        {
            _prices[symbol] = price;
        }
    }

    public string Name => "Mock Feed";
    public MarketFeedStatus Status { get; private set; } = MarketFeedStatus.Stopped;
    public event EventHandler? StatusChanged;

    public void Start(TimeSpan? interval = null)
    {
        _timer?.Dispose();
        SetStatus(MarketFeedStatus.Connected);
        _timer = new Timer(_ => PublishTicks(), null, TimeSpan.Zero, interval ?? TimeSpan.FromSeconds(2));
    }

    void IMarketDataFeed.Start() => Start();

    private void PublishTicks()
    {
        foreach (var (symbol, price) in _prices.ToList())
        {
            var delta = (decimal)(_random.NextDouble() - 0.5) * price * 0.0008m;
            var next = Math.Max(0.01m, price + delta);
            _prices[symbol] = next;
            var spread = next * 0.0001m;
            _bus.Publish(new MarketTickEvent(symbol, next, next - spread, next + spread));
        }
    }

    private void SetStatus(MarketFeedStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        SetStatus(MarketFeedStatus.Stopped);
    }
}
