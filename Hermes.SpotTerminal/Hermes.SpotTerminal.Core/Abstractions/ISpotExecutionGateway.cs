using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;

namespace Hermes.SpotTerminal.Core.Abstractions;

public interface ISpotExecutionGateway
{
    Task<IReadOnlyList<SpotBalance>> GetBalancesAsync(CancellationToken ct = default);
    Task<SpotOrder> PlaceOrderAsync(string symbol, SpotOrderType type, SpotOrderSide side, decimal quantity, decimal price, CancellationToken ct = default);
    Task<bool> CancelOrderAsync(string symbol, string orderId, CancellationToken ct = default);
    Task<IReadOnlyList<SpotOrder>> GetOpenOrdersAsync(CancellationToken ct = default);
}
