using Hermes.SpotTerminal.Core.Abstractions;
using Hermes.SpotTerminal.Core.Domain;
using Hermes.SpotTerminal.Core.Enums;
using Hermes.SpotTerminal.Core.Events;

namespace Hermes.SpotTerminal.Exchange.Virtual;

public sealed class VirtualSpotExchange : ISpotExecutionGateway
{
    private readonly ISpotStateStore _store;
    private readonly IEventBus _bus;
    private int _orderSeq = 2000;

    public VirtualSpotExchange(ISpotStateStore store, IEventBus bus)
    {
        _store = store;
        _bus = bus;
    }

    public Task<IReadOnlyList<SpotBalance>> GetBalancesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SpotBalance>>(_store.Snapshot.Balances);

    public Task<IReadOnlyList<SpotOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SpotOrder>>(
            _store.Snapshot.Orders.Where(o => o.Status == SpotOrderStatus.Open).ToList());

    public Task<SpotOrder> PlaceOrderAsync(string symbol, SpotOrderType type, SpotOrderSide side, decimal quantity, decimal price, CancellationToken ct = default)
    {
        var snap = _store.Snapshot;
        var ticker = snap.Tickers.FirstOrDefault(t => t.Symbol == symbol);
        var fillPrice = type == SpotOrderType.Market ? (ticker?.Price ?? price) : price;
        if (fillPrice <= 0)
        {
            var rejected = new SpotOrder
            {
                Id = $"s-{_orderSeq++}",
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
                Source = "Exchange",
                Message = $"No price for {symbol}",
            }));
            return Task.FromResult(rejected);
        }

        var notional = fillPrice * quantity;
        var quote = symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? "USDT" : "USDT";
        var baseAsset = symbol.Replace("USDT", "", StringComparison.OrdinalIgnoreCase);

        _store.Mutate(s =>
        {
            var usdt = s.Balances.FirstOrDefault(b => b.Asset == quote);
            var baseBal = s.Balances.FirstOrDefault(b => b.Asset == baseAsset);
            if (usdt is null)
            {
                usdt = new SpotBalance { Asset = quote };
                s.Balances.Add(usdt);
            }

            if (baseBal is null)
            {
                baseBal = new SpotBalance { Asset = baseAsset };
                s.Balances.Add(baseBal);
            }

            if (side == SpotOrderSide.Buy)
            {
                if (usdt.Free < notional)
                {
                    return;
                }

                usdt.Free -= notional;
                baseBal.Free += quantity;
            }
            else
            {
                if (baseBal.Free < quantity)
                {
                    return;
                }

                baseBal.Free -= quantity;
                usdt.Free += notional;
            }
        });

        var order = new SpotOrder
        {
            Id = $"s-{_orderSeq++}",
            Symbol = symbol,
            Type = type,
            Side = side,
            Price = fillPrice,
            Quantity = quantity,
            Status = SpotOrderStatus.Filled,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _store.Mutate(s => s.Orders.Insert(0, order));
        _bus.Publish(new OrderPlacedEvent(order));
        _bus.Publish(new OrderFilledEvent(order));
        _bus.Publish(new BalancesUpdatedEvent());
        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "OrderFilled",
            Source = "Exchange",
            Message = $"{order.Symbol} {order.Side} {order.Quantity} @ {order.Price:N2}",
        }));

        return Task.FromResult(order);
    }

    public Task<bool> CancelOrderAsync(string symbol, string orderId, CancellationToken ct = default)
    {
        var ok = false;
        _store.Mutate(s =>
        {
            var o = s.Orders.FirstOrDefault(x => x.Id == orderId && x.Status == SpotOrderStatus.Open);
            if (o is null)
            {
                return;
            }

            o.Status = SpotOrderStatus.Cancelled;
            ok = true;
        });

        if (ok)
        {
            _bus.Publish(new OrderCancelledEvent(orderId));
        }

        return Task.FromResult(ok);
    }
}
