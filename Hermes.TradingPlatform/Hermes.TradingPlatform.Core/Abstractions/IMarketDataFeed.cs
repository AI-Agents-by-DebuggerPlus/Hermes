using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Abstractions;

public interface IMarketDataFeed : IDisposable
{
    string Name { get; }
    MarketFeedStatus Status { get; }
    event EventHandler? StatusChanged;
    void Start();
}
