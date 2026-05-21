using Hermes.TradingPlatform.Core.Domain;

namespace Hermes.TradingPlatform.Core.Abstractions;

public interface IRiskValidator
{
    (bool Allowed, string? Reason) ValidateNewOrder(TradingPlatformState state, Order order);
}
