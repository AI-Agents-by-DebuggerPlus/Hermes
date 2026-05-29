using Binance.Net.Enums;
using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;
using SpotOrderType = Hermes.SpotTerminal.Core.Enums.SpotOrderType;

namespace Hermes.SpotTerminal.Exchange.Binance;

public sealed class BinanceSpotExecutionGateway : ISpotExecutionGateway, IDisposable
{
    private readonly global::Binance.Net.Clients.BinanceRestClient _client;
    private readonly IEventBus _bus;
    private readonly ISpotStateStore _store;

    public BinanceSpotExecutionGateway(string apiKey, string apiSecret, IEventBus bus, ISpotStateStore store)
    {
        _client = BinanceSpotClientFactory.CreateRest(apiKey, apiSecret);
        _bus = bus;
        _store = store;
    }

    public async Task<IReadOnlyList<SpotBalance>> GetBalancesAsync(CancellationToken ct = default)
    {
        var result = await _client.SpotApi.Account.GetBalancesAsync(ct: ct).ConfigureAwait(false);
        if (!result.Success || result.Data is null)
        {
            return [];
        }

        var balances = result.Data
            .Where(b => b.Total > 0)
            .Select(b => new SpotBalance { Asset = b.Asset, Free = b.Available, Locked = b.Locked })
            .ToList();

        _store.Mutate(s =>
        {
            s.Balances.Clear();
            s.Balances.AddRange(balances);
        });

        return balances;
    }

    public async Task<IReadOnlyList<SpotOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        var result = await _client.SpotApi.Trading.GetOpenOrdersAsync(ct: ct).ConfigureAwait(false);
        if (!result.Success || result.Data is null)
        {
            return [];
        }

        return result.Data.Select(MapOrder).ToList();
    }

    public async Task<SpotOrder> PlaceOrderAsync(string symbol, SpotOrderType type, SpotOrderSide side, decimal quantity, decimal price, CancellationToken ct = default)
    {
        var orderSide = side == SpotOrderSide.Buy ? OrderSide.Buy : OrderSide.Sell;
        var binanceType = type == SpotOrderType.Market
            ? global::Binance.Net.Enums.SpotOrderType.Market
            : global::Binance.Net.Enums.SpotOrderType.Limit;

        var result = type == SpotOrderType.Market
            ? await _client.SpotApi.Trading.PlaceOrderAsync(symbol, orderSide, binanceType, quantity: quantity, ct: ct).ConfigureAwait(false)
            : await _client.SpotApi.Trading.PlaceOrderAsync(symbol, orderSide, binanceType, quantity: quantity, price: price, ct: ct).ConfigureAwait(false);

        if (!result.Success || result.Data is null)
        {
            var rejected = new SpotOrder
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Symbol = symbol,
                Type = type,
                Side = side,
                Price = price,
                Quantity = quantity,
                Status = SpotOrderStatus.Rejected,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                EventType = "OrderRejected",
                Source = "Binance",
                Message = result.Error?.Message ?? "Unknown error",
            }));
            return rejected;
        }

        var order = MapPlacedOrder(result.Data);
        _store.Mutate(s => s.Orders.Insert(0, order));
        _bus.Publish(new OrderPlacedEvent(order));
        if (order.Status == SpotOrderStatus.Filled)
        {
            _bus.Publish(new OrderFilledEvent(order));
        }

        await GetBalancesAsync(ct).ConfigureAwait(false);
        return order;
    }

    public async Task<bool> CancelOrderAsync(string symbol, string orderId, CancellationToken ct = default)
    {
        if (!long.TryParse(orderId, out var id))
        {
            return false;
        }

        var result = await _client.SpotApi.Trading.CancelOrderAsync(symbol, id, ct: ct).ConfigureAwait(false);
        if (result.Success)
        {
            _bus.Publish(new OrderCancelledEvent(orderId));
        }

        return result.Success;
    }

    private static SpotOrder MapPlacedOrder(global::Binance.Net.Objects.Models.Spot.BinancePlacedOrder o) => new()
    {
        Id = o.Id.ToString(),
        Symbol = o.Symbol,
        Type = o.Type == global::Binance.Net.Enums.SpotOrderType.Market ? SpotOrderType.Market : SpotOrderType.Limit,
        Side = o.Side == OrderSide.Buy ? SpotOrderSide.Buy : SpotOrderSide.Sell,
        Price = o.Price,
        Quantity = o.Quantity,
        Status = o.Status == OrderStatus.Filled ? SpotOrderStatus.Filled
            : o.Status == OrderStatus.Canceled ? SpotOrderStatus.Cancelled
            : SpotOrderStatus.Open,
        CreatedAt = new DateTimeOffset(o.CreateTime),
    };

    private static SpotOrder MapOrder(global::Binance.Net.Objects.Models.Spot.BinanceOrder o) => new()
    {
        Id = o.Id.ToString(),
        Symbol = o.Symbol,
        Type = o.Type == global::Binance.Net.Enums.SpotOrderType.Market ? SpotOrderType.Market : SpotOrderType.Limit,
        Side = o.Side == OrderSide.Buy ? SpotOrderSide.Buy : SpotOrderSide.Sell,
        Price = o.Price,
        Quantity = o.Quantity,
        Status = o.Status == OrderStatus.Filled ? SpotOrderStatus.Filled
            : o.Status == OrderStatus.Canceled ? SpotOrderStatus.Cancelled
            : SpotOrderStatus.Open,
        CreatedAt = o.CreateTime,
    };

    public void Dispose() => _client.Dispose();
}
