using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Abstractions;

public interface IVirtualExchange
{
    Order PlaceOrder(string symbol, OrderType type, OrderSide side, decimal quantity, decimal price, bool reduceOnly);
    /// <summary>Market reduce-only close for the full position (or <paramref name="quantity"/> if set).</summary>
    Order ClosePosition(string symbol, decimal? quantity = null);
    bool TryCancelOrder(string orderId);
    int NextOrderSequence { get; }
    void RestoreOrderSequence(int nextSequence);
}
