using Hermes.TradingPlatform.Core.Abstractions;

using Hermes.TradingPlatform.Core.Domain;

using Hermes.TradingPlatform.Core.Events;

using Hermes.TradingPlatform.Core.State;



namespace Hermes.TradingPlatform.Exchange;



public sealed class VirtualExchangeEngine : IVirtualExchange, IDisposable

{

    private const decimal TakerFeeRate = 0.0004m;

    private const decimal MarketSlippageRate = 0.0002m;



    private readonly ITradingStateStore _store;

    private readonly IEventBus _bus;

    private readonly IRiskValidator _risk;

    private int _orderSequence = 1003;



    public VirtualExchangeEngine(ITradingStateStore store, IEventBus bus, IRiskValidator risk)

    {

        _store = store;

        _bus = bus;

        _risk = risk;

        _bus.Subscribe<MarketTickEvent>(OnMarketTick);

    }



    public Order PlaceOrder(string symbol, OrderType type, OrderSide side, decimal quantity, decimal price, bool reduceOnly)

    {

        var order = new Order

        {

            Id = $"o-{Interlocked.Increment(ref _orderSequence)}",

            Symbol = symbol,

            Type = type,

            Side = side,

            Price = price,

            Quantity = quantity,

            Status = OrderStatus.Open,

            ReduceOnly = reduceOnly,

        };



        var snapshot = _store.Snapshot;

        var validation = _risk.ValidateNewOrder(snapshot, order);

        if (!validation.Allowed)

        {

            order.Status = OrderStatus.Rejected;

            _store.Mutate(s => s.Orders.Insert(0, order));

            _bus.Publish(new PlatformLogEvent(new PlatformLogEntry

            {

                Timestamp = DateTimeOffset.UtcNow,

                EventType = "Risk",

                Source = "RiskManager",

                Message = $"Order rejected: {validation.Reason}",

            }));

            return order;

        }



        _store.Mutate(s => s.Orders.Insert(0, order));

        _bus.Publish(new OrderPlacedEvent(order));



        if (type == OrderType.Market)

        {

            var ticker = snapshot.Tickers.FirstOrDefault(t => t.Symbol == symbol);

            var fillPrice = ApplySlippage(ticker?.Price ?? price, side);

            FillOrder(order.Id, fillPrice);

        }



        return order;

    }

    public Order ClosePosition(string symbol, decimal? quantity = null)
    {
        var snapshot = _store.Snapshot;
        var pos = snapshot.Positions.FirstOrDefault(p =>
            string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (pos is null || pos.Size <= 0)
        {
            return new Order
            {
                Id = $"o-{Interlocked.Increment(ref _orderSequence)}",
                Symbol = symbol,
                Type = OrderType.Market,
                Side = OrderSide.Sell,
                Status = OrderStatus.Rejected,
                Quantity = 0,
            };
        }

        var closeSide = pos.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        var closeQty = quantity is > 0 ? Math.Min(quantity.Value, pos.Size) : pos.Size;
        var ticker = snapshot.Tickers.FirstOrDefault(t =>
            string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var price = ticker?.Price ?? pos.MarkPrice;
        return PlaceOrder(symbol, OrderType.Market, closeSide, closeQty, price, reduceOnly: true);
    }

    public int NextOrderSequence => _orderSequence;

    public void RestoreOrderSequence(int nextSequence) =>
        Interlocked.Exchange(ref _orderSequence, Math.Max(nextSequence, 1003));

    public Order? ModifyOrder(string orderId, decimal newPrice, decimal newQuantity, decimal? newTrigger)
    {
        // Modify is implemented as cancel-and-replace. The new order is re-validated by the
        // risk gate, so all caps (leverage, exposure, daily loss) apply to the modification.
        var snapshot = _store.Snapshot;
        var existing = snapshot.Orders.FirstOrDefault(o =>
            o.Id == orderId && o.Status == OrderStatus.Open);
        if (existing is null)
        {
            return null;
        }

        if (existing.Type == OrderType.Market)
        {
            // Market orders fill immediately; nothing to modify.
            return null;
        }

        if (newQuantity <= 0 || newPrice <= 0)
        {
            return null;
        }

        if (!TryCancelOrder(orderId))
        {
            return null;
        }

        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "Order",
            Source = "VirtualExchange",
            Message =
                $"Modify {orderId}: {existing.Type} {existing.Side} {existing.Quantity}@{existing.Price:N4} → "
                + $"{newQuantity}@{newPrice:N4}"
                + (newTrigger.HasValue ? $" trigger={newTrigger.Value:N4}" : string.Empty),
        }));

        var replacement = PlaceOrder(
            existing.Symbol,
            existing.Type,
            existing.Side,
            newQuantity,
            newPrice,
            existing.ReduceOnly);

        if (newTrigger.HasValue && replacement.Status != OrderStatus.Rejected)
        {
            _store.Mutate(s =>
            {
                var fresh = s.Orders.FirstOrDefault(o => o.Id == replacement.Id);
                if (fresh is not null)
                {
                    fresh.TriggerPrice = newTrigger;
                }
            });
        }

        return replacement;
    }

    public bool TryCancelOrder(string orderId)

    {

        string? symbol = null;

        _store.Mutate(s =>

        {

            var order = s.Orders.FirstOrDefault(o => o.Id == orderId && o.Status == OrderStatus.Open);

            if (order is null)

            {

                return;

            }



            order.Status = OrderStatus.Cancelled;

            symbol = order.Symbol;

        });



        if (symbol is null)

        {

            return false;

        }



        _bus.Publish(new OrderCancelledEvent(orderId, symbol));

        return true;

    }



    private void OnMarketTick(MarketTickEvent tick)

    {

        var openOrders = _store.Snapshot.Orders

            .Where(o => o.Status == OrderStatus.Open && o.Symbol == tick.Symbol)

            .Select(o => o.Id)

            .ToList();



        foreach (var orderId in openOrders)

        {

            TryFillOnTick(orderId, tick.Price);

        }

    }



    private void TryFillOnTick(string orderId, decimal marketPrice)

    {

        var snapshot = _store.Snapshot;

        var order = snapshot.Orders.FirstOrDefault(o => o.Id == orderId && o.Status == OrderStatus.Open);

        if (order is null)

        {

            return;

        }



        var shouldFill = order.Type switch

        {

            OrderType.Market => true,

            OrderType.Limit => order.Side == OrderSide.Buy

                ? marketPrice <= order.Price

                : marketPrice >= order.Price,

            OrderType.Stop => order.Side == OrderSide.Buy

                ? marketPrice >= (order.TriggerPrice ?? order.Price)

                : marketPrice <= (order.TriggerPrice ?? order.Price),

            _ => false,

        };



        if (shouldFill)

        {

            FillOrder(orderId, marketPrice);

        }

    }



    private void FillOrder(string orderId, decimal marketPrice)

    {

        Order? filledOrder = null;

        decimal fillPrice = 0m;

        decimal fee = 0m;

        decimal realized = 0m;

        decimal balanceBefore = 0m;

        decimal balanceAfter = 0m;

        string journalKind = "Fill";



        _store.Mutate(s =>

        {

            var order = s.Orders.FirstOrDefault(o => o.Id == orderId && o.Status == OrderStatus.Open);

            if (order is null)

            {

                return;

            }



            balanceBefore = s.Account.Balance;

            fillPrice = order.Type == OrderType.Market

                ? ApplySlippage(marketPrice, order.Side)

                : order.Price;



            fee = fillPrice * order.Quantity * TakerFeeRate;

            order.Status = OrderStatus.Filled;

            order.Price = fillPrice;

            filledOrder = order;



            var impact = ApplyFillToPosition(s, order, fillPrice);

            realized = impact.RealizedPnl;

            journalKind = impact.JournalKind;



            s.Account.Balance += realized;

            s.Account.Balance -= fee;



            if (realized != 0)

            {

                s.Pnl.Today += realized;

                s.Pnl.Week += realized;

                s.Pnl.Month += realized;

                s.Pnl.AllTime += realized;

            }



            s.Pnl.Today -= fee;

            TradingStateCalculator.RecalculateEquity(s);

            balanceAfter = s.Account.Balance;

        });



        if (filledOrder is null)

        {

            return;

        }



        _bus.Publish(new OrderFilledEvent(

            filledOrder,

            fillPrice,

            fee,

            realized,

            balanceBefore,

            balanceAfter,

            journalKind));



        var ro = filledOrder.ReduceOnly ? " RO" : string.Empty;

        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry

        {

            Timestamp = DateTimeOffset.UtcNow,

            EventType = "Fill",

            Source = "VirtualExchange",

            Message =

                $"Filled {filledOrder.Id} {filledOrder.Symbol} {filledOrder.Side} qty={filledOrder.Quantity} @ {fillPrice:N2}{ro} | "

                + $"realized={realized:N4} fee={fee:N4} | balance {balanceBefore:N2} → {balanceAfter:N2} ({journalKind})",

        }));



        if (realized != 0 && journalKind == "Close")

        {

            _bus.Publish(new PositionClosedEvent(filledOrder.Symbol, realized));

        }



        TryAttachDefaultSlTp(filledOrder, fillPrice, journalKind);

    }



    private void TryAttachDefaultSlTp(Order filledOrder, decimal fillPrice, string journalKind)
    {
        if (filledOrder.ReduceOnly || journalKind != "Open")
        {
            return;
        }

        var snapshot = _store.Snapshot;
        var risk = snapshot.Risk;
        if (!risk.AutoApplyDefaultSlTp)
        {
            return;
        }

        var balance = snapshot.Account.Balance;
        var riskPct = risk.MaxRiskPerTradePercent;
        var tpMult = risk.DefaultTakeProfitRrMultiplier;
        if (balance <= 0 || riskPct <= 0 || tpMult <= 0 || filledOrder.Quantity <= 0)
        {
            return;
        }

        var riskAmount = balance * (riskPct / 100m);
        var slDistance = riskAmount / filledOrder.Quantity;
        if (slDistance <= 0)
        {
            return;
        }

        var isLong = filledOrder.Side == OrderSide.Buy;
        var slPrice = isLong ? fillPrice - slDistance : fillPrice + slDistance;
        var tpPrice = isLong ? fillPrice + slDistance * tpMult : fillPrice - slDistance * tpMult;
        if (slPrice <= 0 || tpPrice <= 0)
        {
            return;
        }

        var exitSide = isLong ? OrderSide.Sell : OrderSide.Buy;
        var qty = filledOrder.Quantity;

        var sl = PlaceOrder(filledOrder.Symbol, OrderType.Stop, exitSide, qty, slPrice, reduceOnly: true);
        var tp = PlaceOrder(filledOrder.Symbol, OrderType.Limit, exitSide, qty, tpPrice, reduceOnly: true);

        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = "AutoSlTp",
            Source = "VirtualExchange",
            Message =
                $"Auto SL/TP for {filledOrder.Id} ({filledOrder.Symbol} {filledOrder.Side} qty={qty} @ {fillPrice:N2}): "
                + $"SL {sl.Id} @ {slPrice:N2} ({riskPct:N2}% risk = {slDistance:N2}); "
                + $"TP {tp.Id} @ {tpPrice:N2} (× {tpMult:N1})",
        }));
    }



    private static PositionFillImpact ApplyFillToPosition(TradingPlatformState state, Order order, decimal fillPrice)

    {

        if (order.ReduceOnly)

        {

            var positionSideToClose = order.Side == OrderSide.Buy ? PositionSide.Short : PositionSide.Long;

            var pos = state.Positions.FirstOrDefault(p =>

                p.Symbol == order.Symbol && p.Side == positionSideToClose);

            if (pos is null)

            {

                return new PositionFillImpact(0m, "Reduce");

            }



            var sizeBefore = pos.Size;

            var realized = ReducePosition(state, pos, order.Quantity, fillPrice);

            var closedQty = Math.Min(order.Quantity, sizeBefore);

            var kind = closedQty >= sizeBefore - 0.00000001m ? "Close" : "Reduce";

            return new PositionFillImpact(realized, kind);

        }



        var openSide = order.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;

        var existing = state.Positions.FirstOrDefault(p => p.Symbol == order.Symbol && p.Side == openSide);

        if (existing is null)

        {

            state.Positions.Add(new Position

            {

                Symbol = order.Symbol,

                Side = openSide,

                Size = order.Quantity,

                EntryPrice = fillPrice,

                MarkPrice = fillPrice,

                UnrealizedPnl = 0m,

                RealizedPnl = 0m,

            });

            return new PositionFillImpact(0m, "Open");

        }



        var totalSize = existing.Size + order.Quantity;

        existing.EntryPrice = ((existing.EntryPrice * existing.Size) + (fillPrice * order.Quantity)) / totalSize;

        existing.Size = totalSize;

        existing.MarkPrice = fillPrice;

        return new PositionFillImpact(0m, "Add");

    }



    private static decimal ReducePosition(TradingPlatformState state, Position pos, decimal qty, decimal fillPrice)

    {

        var closed = Math.Min(qty, pos.Size);

        if (closed <= 0)

        {

            return 0m;

        }



        var pnlPerUnit = pos.Side == PositionSide.Long

            ? fillPrice - pos.EntryPrice

            : pos.EntryPrice - fillPrice;

        var realized = pnlPerUnit * closed;

        pos.RealizedPnl += realized;

        pos.Size -= closed;

        pos.MarkPrice = fillPrice;



        if (pos.Size <= 0.00000001m)

        {

            state.Positions.Remove(pos);

        }



        return realized;

    }



    private static decimal ApplySlippage(decimal price, OrderSide side) =>

        side == OrderSide.Buy

            ? price * (1m + MarketSlippageRate)

            : price * (1m - MarketSlippageRate);



    public void Dispose() { }

}


