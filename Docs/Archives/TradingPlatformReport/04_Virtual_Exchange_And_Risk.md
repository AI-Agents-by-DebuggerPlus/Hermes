# 04. Виртуальная биржа и риск-движок

## 4.1. `VirtualExchangeEngine`

Файл: `Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs`. Имплементирует `IVirtualExchange + IDisposable`. Это **сердце** торговой платформы — paper-биржа с симуляцией.

### 4.1.1. Контракт

```5:21:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Abstractions/IVirtualExchange.cs
public interface IVirtualExchange
{
    Order PlaceOrder(string symbol, OrderType type, OrderSide side, decimal quantity, decimal price, bool reduceOnly);
    Order ClosePosition(string symbol, decimal? quantity = null);
    bool TryCancelOrder(string orderId);
    Order? ModifyOrder(string orderId, decimal newPrice, decimal newQuantity, decimal? newTrigger);

    int NextOrderSequence { get; }
    void RestoreOrderSequence(int nextSequence);
}
```

### 4.1.2. Константы симуляции

```36:39:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    private const decimal TakerFeeRate = 0.0004m;     // 0.04 %
    private const decimal MarketSlippageRate = 0.0002m; // 0.02 %
```

- Применяются ко **всем** Market-ордерам (`ApplySlippage`) и **всем** fills (taker fee). Maker-режима / лимитной комиссии нет.

### 4.1.3. `PlaceOrder` — жизненный цикл

```68:148:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    public Order PlaceOrder(string symbol, OrderType type, OrderSide side, decimal quantity, decimal price, bool reduceOnly)
    {
        var order = new Order { Id = $"o-{Interlocked.Increment(ref _orderSequence)}", ... };

        var snapshot = _store.Snapshot;
        var validation = _risk.ValidateNewOrder(snapshot, order);
        if (!validation.Allowed)
        {
            order.Status = OrderStatus.Rejected;
            _store.Mutate(s => s.Orders.Insert(0, order));
            _bus.Publish(new PlatformLogEvent(new PlatformLogEntry { ..., EventType = "Risk", Source = "RiskManager", ... }));
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
```

**Алгоритм**:

1. Создать `Order` с свежим `Id` (`Interlocked.Increment`).
2. Снять снапшот state, проверить `RiskValidator`.
3. Если отвергнут — статус `Rejected`, вставить в `state.Orders[0]`, опубликовать `PlatformLogEvent (Risk)`. **Не публикуется `OrderPlacedEvent`**, т. е. event log получает только `Risk`-сообщение. (Это нюанс: подписчики `OrderPlacedEvent` не узнают о rejected-ордере.)
4. Если разрешён — добавить в state, опубликовать `OrderPlacedEvent`.
5. Если `OrderType.Market` — немедленно заполнить по `(ticker?.Price ?? price) ± slippage`.

⚠️ Цена fill для market берётся **из последнего тикера в snapshot**. Если тикер ещё не получен (например, сразу после старта) — fill пойдёт по `price` из аргумента (обычно 0 для market). См. `OrdersViewModel.PlaceNewOrder`, который сам подставляет ticker?.Price.

### 4.1.4. `OnMarketTick` — попытка fill открытых ордеров

Подписан на `MarketTickEvent`. Для каждого открытого ордера по символу вычисляет:

```337:357:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
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
```

Логика:
- **Limit**: Buy fills, когда цена опустилась до или ниже limit. Sell — когда поднялась выше.
- **Stop**: Buy stop срабатывает на росте до триггера; Sell stop — на падении до триггера. (Это классический stop-market: после срабатывания заполняется по marketPrice.)
- **Market** (как fallback) — заполнится сразу.

### 4.1.5. `FillOrder` — экономика fill

Главная функция. Под единым `_store.Mutate`:

1. Найти ордер по id с `Status=Open`.
2. `balanceBefore = s.Account.Balance`.
3. Вычислить `fillPrice`:
   - Market → `ApplySlippage(marketPrice, side)`
   - Limit / Stop → `order.Price` (то, что в book; здесь нет slippage)
4. Вычислить `fee = fillPrice * Quantity * 0.0004`.
5. Поменять статус ордера на `Filled`, переписать `order.Price = fillPrice`.
6. Вызвать `ApplyFillToPosition(...)` → получаем `(RealizedPnl, JournalKind)`.
7. `Balance += RealizedPnl - fee`.
8. Обновить `PnlTracker.{Today,Week,Month,AllTime}` на `realized`. `Today -= fee`.
9. `TradingStateCalculator.RecalculateEquity(s)`.
10. После мутации — публикация `OrderFilledEvent`, `PlatformLogEvent("Fill")`, опционально `PositionClosedEvent`, попытка `TryAttachDefaultSlTp`.

### 4.1.6. `ApplyFillToPosition` — изменение позиции

```592:674:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    private static PositionFillImpact ApplyFillToPosition(TradingPlatformState state, Order order, decimal fillPrice)
    {
        if (order.ReduceOnly)
        {
            var positionSideToClose = order.Side == OrderSide.Buy ? PositionSide.Short : PositionSide.Long;
            var pos = state.Positions.FirstOrDefault(p =>
                p.Symbol == order.Symbol && p.Side == positionSideToClose);
            if (pos is null) return new PositionFillImpact(0m, "Reduce");

            var sizeBefore = pos.Size;
            var realized = ReducePosition(state, pos, order.Quantity, fillPrice);
            var closedQty = Math.Min(order.Quantity, sizeBefore);
            var kind = closedQty >= sizeBefore - 0.00000001m ? "Close" : "Reduce";
            return new PositionFillImpact(realized, kind);
        }

        var openSide = order.Side == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
        var existing = state.Positions.FirstOrDefault(p => p.Symbol == order.Symbol && p.Side == openSide);
        if (existing is null) { state.Positions.Add(new Position { ... }); return new PositionFillImpact(0m, "Open"); }

        var totalSize = existing.Size + order.Quantity;
        existing.EntryPrice = ((existing.EntryPrice * existing.Size) + (fillPrice * order.Quantity)) / totalSize;
        existing.Size = totalSize;
        existing.MarkPrice = fillPrice;
        return new PositionFillImpact(0m, "Add");
    }
```

**Ключевые наблюдения**:
- **Hedge-mode**: позиции ищутся по `(Symbol, Side)`. Buy на ETHUSDT откроет Long независимо от того, есть ли уже Short.
- **Reduce-only**: ищется позиция **противоположной** стороны (Buy reduce → закрывает Short).
- **Weighted average entry price** при Add.
- **Allow "no-op" reduce**: если reduce-only попал по символу без позиции — `PositionFillImpact(0, "Reduce")`, но `Status=Filled` всё равно. Это значит, что **fee всё равно спишется**, а реальной экономики не будет. На практике `RiskValidator` сюда не пускает (см. ниже).

### 4.1.7. `ReducePosition`

```678:722:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    private static decimal ReducePosition(TradingPlatformState state, Position pos, decimal qty, decimal fillPrice)
    {
        var closed = Math.Min(qty, pos.Size);
        if (closed <= 0) return 0m;

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
```

Если after-reduce `Size ≤ 1e-8` — позиция удаляется из списка (на этом основании выставляется `Kind="Close"`).

### 4.1.8. `TryAttachDefaultSlTp` — авто SL/TP

Срабатывает только при `journalKind == "Open"` (т. е. на полностью новом открытии позиции) и `risk.AutoApplyDefaultSlTp = true`.

Формула:
```
riskAmount = Balance * (MaxRiskPerTradePercent / 100)
slDistance = riskAmount / Quantity
slPrice    = isLong ? fillPrice - slDistance : fillPrice + slDistance
tpPrice    = isLong ? fillPrice + slDistance * tpMult : fillPrice - slDistance * tpMult
```

Затем `PlaceOrder(symbol, Stop, exitSide, qty, slPrice, reduceOnly: true)` и `PlaceOrder(symbol, Limit, exitSide, qty, tpPrice, reduceOnly: true)`. Лог-событие `EventType="AutoSlTp"`.

⚠️ **Эти 2 PlaceOrder выполняются под локом state**? Нет — `_store.Mutate` уже завершён к моменту `TryAttachDefaultSlTp`. PlaceOrder делает свои отдельные мутации. Но **внутри стека публикатора `OrderFilledEvent`**: получается, что `OrderFilledEvent` ещё не закончил обходить подписчиков, а уже публикуются `OrderPlacedEvent` × 2. Это потенциальный re-entry hazard для подписчиков, которые ведут счётчики.

### 4.1.9. `ModifyOrder` — cancel-and-replace

```183:243:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    public Order? ModifyOrder(string orderId, decimal newPrice, decimal newQuantity, decimal? newTrigger)
    {
        var existing = snapshot.Orders.FirstOrDefault(o => o.Id == orderId && o.Status == OrderStatus.Open);
        if (existing is null) return null;
        if (existing.Type == OrderType.Market) return null;
        if (newQuantity <= 0 || newPrice <= 0) return null;

        if (!TryCancelOrder(orderId)) return null;

        _bus.Publish(new PlatformLogEvent(new PlatformLogEntry { ..., Message = "Modify ... → ..." }));

        var replacement = PlaceOrder(existing.Symbol, existing.Type, existing.Side, newQuantity, newPrice, existing.ReduceOnly);

        if (newTrigger.HasValue && replacement.Status != OrderStatus.Rejected)
        {
            _store.Mutate(s => { var fresh = s.Orders.FirstOrDefault(o => o.Id == replacement.Id); fresh.TriggerPrice = newTrigger; });
        }

        return replacement;
    }
```

- Возвращает `null` для market-ордеров и невалидных параметров (отличается от Reject — UI должен показать предупреждение).
- TriggerPrice патчится **отдельной мутацией после успешного PlaceOrder**.
- `OrderPlacedEvent` и `OrderCancelledEvent` событий **публикуются оба** — фактически, modify не атомарен.

### 4.1.10. `ClosePosition`

```152:176:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/VirtualExchangeEngine.cs
    public Order ClosePosition(string symbol, decimal? quantity = null)
    {
        var pos = snapshot.Positions.FirstOrDefault(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (pos is null || pos.Size <= 0) return new Order { ..., Status = OrderStatus.Rejected, Quantity = 0, };

        var closeSide = pos.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        var closeQty = quantity is > 0 ? Math.Min(quantity.Value, pos.Size) : pos.Size;
        var price = ticker?.Price ?? pos.MarkPrice;
        return PlaceOrder(symbol, OrderType.Market, closeSide, closeQty, price, reduceOnly: true);
    }
```

- Закрывает **первую найденную** позицию по символу. В hedge-mode при двух позициях Long+Short закроется только одна (тот, что находится первым в `state.Positions`). Это **известное ограничение**, в текущем UI hedge не вызывается из паблик-команд.

## 4.2. `RiskValidator`

Файл: `Hermes.TradingPlatform.Risk/RiskValidator.cs`. Имплементирует `IRiskValidator`. Все проверки **синхронны** перед `OrderPlaced`.

### 4.2.1. Каскад проверок

```8:99:Hermes.TradingPlatform/Hermes.TradingPlatform.Risk/RiskValidator.cs
    public (bool Allowed, string? Reason) ValidateNewOrder(TradingPlatformState state, Order order)
    {
        if (state.Risk.EmergencyHalt) return (false, "Emergency halt active — no new orders.");
        if (state.Risk.SafeMode && !order.ReduceOnly) return (false, "Safe mode: only reduce-only orders allowed.");
        if (state.Account.Leverage > state.Risk.MaxLeverage) return (false, $"Leverage ... exceeds max ...");
        if (order.Symbol == "BTCUSDT" && order.Quantity > state.Risk.MaxPositionSizeBtc) return (false, ...);

        if (order.ReduceOnly)
        {
            var positionSide = order.Side == OrderSide.Buy ? PositionSide.Short : PositionSide.Long;
            var pos = state.Positions.FirstOrDefault(p => p.Symbol == order.Symbol && p.Side == positionSide);
            if (pos is null || pos.Size <= 0) return (false, $"Reduce-only ...: no {positionSide} position ...");
            if (order.Quantity > pos.Size) return (false, $"Reduce qty ... exceeds position size ...");
            return (true, null);
        }

        // Daily-loss circuit breaker
        if (state.Risk.MaxDailyLossPercent > 0 && state.Pnl.Today < 0)
        {
            var startingEquity = state.Account.Balance - state.Pnl.Today;
            if (startingEquity > 0)
            {
                var dailyDrawdownPct = -state.Pnl.Today / startingEquity * 100m;
                if (dailyDrawdownPct >= state.Risk.MaxDailyLossPercent)
                    return (false, $"Daily loss ... reached cap ...");
            }
        }

        // Per-trade margin cap
        if (state.Risk.MaxRiskPerTradePercent > 0 && state.Account.Balance > 0)
        {
            var leverage = state.Account.Leverage > 0 ? state.Account.Leverage : 1m;
            var notional = order.Price * order.Quantity;
            var marginRequired = notional / leverage;
            var capUsd = state.Account.Balance * (state.Risk.MaxRiskPerTradePercent / 100m);
            if (capUsd > 0 && marginRequired > capUsd) return (false, $"Per-trade margin ... exceeds cap ...");
        }

        // Total exposure cap
        if (state.Risk.MaxExposurePercent > 0 && state.Account.Balance > 0)
        {
            var existingMargin = state.Positions.Sum(p => Math.Abs(p.Size * p.MarkPrice)) / leverage;
            var orderMargin = order.Price * order.Quantity / leverage;
            var capUsd = state.Account.Balance * (state.Risk.MaxExposurePercent / 100m);
            if (capUsd > 0 && existingMargin + orderMargin > capUsd) return (false, $"Total exposure ... exceeds cap ...");
        }

        return (true, null);
    }
```

| Проверка | Условие отказа | Реакция |
|---|---|---|
| **EmergencyHalt** | `Risk.EmergencyHalt` | Reject с `"Emergency halt active"` |
| **SafeMode** | `Risk.SafeMode && !ReduceOnly` | Reject — разрешены только reduce-only |
| **MaxLeverage** | `Account.Leverage > Risk.MaxLeverage` | Reject |
| **MaxPositionSizeBtc** | `Symbol="BTCUSDT" && Quantity > MaxPositionSizeBtc` | Reject (только для BTC!) |
| **Reduce-only consistency** | Нет противоположной позиции или qty > size | Reject; иначе skip остальные проверки |
| **MaxDailyLossPercent** | `−Pnl.Today / (Balance−Pnl.Today) * 100 ≥ MaxDailyLossPercent` | Reject (auto-shutdown отдельно) |
| **MaxRiskPerTradePercent** | `Price*Qty/Leverage > Balance*pct/100` | Reject |
| **MaxExposurePercent** | `(Σ|Size*MarkPrice| + Price*Qty)/Leverage > Balance*pct/100` | Reject |

**Замечание про market-ордера**: для Market `order.Price` = 0 на момент `RiskValidator.ValidateNewOrder` (см. `PlaceOrder`). Это значит, что проверки `MaxRiskPerTradePercent` и `MaxExposurePercent` для market-ордеров **прохождают как allowed** (notional=0, margin=0). Это **значимая дыра** в risk-каркасе — крупный market sell может пройти без проверки exposure. (См. секцию `10.3` в Findings.)

**Замечание про MaxPositionSizeBtc**: проверка делается только для `"BTCUSDT"` (case-sensitive). ETH/SOL/прочие — не ограничены этим параметром. Имя поля честно говорит "Btc", но это **жёстко зашитый символ**.

## 4.3. `RiskCircuitBreaker`

Файл: `Hermes.TradingPlatform.Risk/RiskCircuitBreaker.cs`. Это **второй уровень** защиты, работающий **постфактум** (после изменения state, не до).

### 4.3.1. Подписки

```19:26:Hermes.TradingPlatform/Hermes.TradingPlatform.Risk/RiskCircuitBreaker.cs
    public RiskCircuitBreaker(ITradingStateStore store, IEventBus bus)
    {
        _store = store;
        _bus = bus;
        _store.StateChanged += (_, _) => Evaluate();
        _bus.Subscribe<OrderFilledEvent>(_ => Evaluate());
        _bus.Subscribe<MarketTickEvent>(_ => Evaluate());
    }
```

`Evaluate` вызывается на **каждое** изменение state, **каждый** fill и **каждый** тик. На high-frequency Binance это может быть 50+ раз в секунду. Внутри — cheap pre-check на `EmergencyHalt || !AutoShutdown`.

### 4.3.2. Условия trip

```82:108:Hermes.TradingPlatform/Hermes.TradingPlatform.Risk/RiskCircuitBreaker.cs
    private static (bool Trip, string? Reason) ShouldTrip(TradingPlatformState s)
    {
        if (s.Risk.MaxDailyLossPercent > 0 && s.Pnl.Today < 0)
        {
            var dd = -s.Pnl.Today / (s.Account.Balance - s.Pnl.Today) * 100m;
            if (dd >= s.Risk.MaxDailyLossPercent)
                return (true, $"daily loss {dd:F2}% ≥ cap {s.Risk.MaxDailyLossPercent:F2}%");
        }

        if (s.Risk.MaxExposurePercent > 0 && s.Account.Balance > 0)
        {
            var marginUsed = s.Positions.Sum(p => Math.Abs(p.Size * p.MarkPrice)) / leverage;
            var capUsd = s.Account.Balance * (s.Risk.MaxExposurePercent / 100m);
            if (capUsd > 0 && marginUsed > capUsd * 1.05m)
                return (true, $"exposure {marginUsed:N2} > cap {capUsd:N2} ...");
        }

        return (false, null);
    }
```

При trip:
- Под локом: `Risk.EmergencyHalt = true`, `Risk.RiskLevel = Critical`, все стратегии → `Halted`.
- Публикует `RiskTriggeredEvent(reason, EmergencyHalt: true)`.
- Публикует `PlatformLogEvent` "Auto-shutdown engaged: ...".

`_alreadyTripped` под отдельным локом защищает от повторного публикации. `Rearm()` снимает флаг (но не сам `EmergencyHalt` — его UI снимает через `RiskManagerViewModel`).

### 4.3.3. **Важное наблюдение про exposure**

В `RiskCircuitBreaker.ShouldTrip` exposure trip срабатывает при `marginUsed > capUsd * 1.05` (то есть с **5% запасом**). В `RiskValidator` — без запаса. Это потенциальная инконсистентность: если открытие ордера прошло на грани 99% cap, но затем тик подвинул `MarkPrice` так, что exposure вырос на >5% — сработает auto-shutdown. Не баг, но **разные пороги** на пред- и постфактум.

## 4.4. Двухуровневая защита

```
   Pre-trade (synchronous, blocking PlaceOrder)
   ┌──────────────────────────────────────────────┐
   │  RiskValidator.ValidateNewOrder              │
   │   • EmergencyHalt                            │
   │   • SafeMode (reduce-only)                   │
   │   • MaxLeverage                              │
   │   • MaxPositionSizeBtc (только BTC)          │
   │   • Reduce-only consistency                  │
   │   • MaxDailyLossPercent                      │
   │   • MaxRiskPerTradePercent (по margin)       │
   │   • MaxExposurePercent (по margin)           │
   └──────────────────────────────────────────────┘
                       │
                       ▼ если allowed → OrderPlaced/Filled
                       │
   Post-trade (async, reactive)
   ┌──────────────────────────────────────────────┐
   │  RiskCircuitBreaker.Evaluate                 │
   │   • DailyDrawdownPercent ≥ MaxDailyLoss      │
   │   • Exposure > Cap * 1.05 (with grace)       │
   │   → EmergencyHalt + Strategies.Status=Halted │
   │   → RiskTriggeredEvent + Log                 │
   └──────────────────────────────────────────────┘
```

После trip ни один новый ордер (кроме reduce-only при SafeMode=true, но `EmergencyHalt` блокирует и их) не пройдёт. UI оператор должен **вручную** снять `EmergencyHalt` через Risk Manager → потом дёрнуть `RiskCircuitBreaker.Rearm()` (но этот метод **не подключён к UI**! см. Findings).

## 4.5. Что хочется зафиксировать в эпизодах для Hermes (ExperienceExporter)

Из AUDIT-комментариев в `VirtualExchangeEngine.cs` и `RiskProfile.cs`:

| Событие на бирже | Видно ли в `bridge/snapshot.json`? |
|---|---|
| `OrderPlacedEvent` | Косвенно: новый ордер появляется в `snapshot.Orders[Status=Open]` |
| `OrderFilledEvent` (со всей экономикой) | Только **частично**: позиции, ордера (Filled), PnL, баланс — да; но `Fee`/`Kind`/`BalanceBefore` — **нет** (это есть только в `trade_journal.jsonl`) |
| `OrderCancelledEvent` | Через `Orders[Status=Cancelled]` |
| `PositionClosedEvent (RealizedPnl)` | Через диф `Pnl.Today`/`Account.Balance` |
| `RiskTriggeredEvent` (`EmergencyHalt`) | `snapshot.Risk.EmergencyHalt` |
| `PlatformLogEvent (Risk/AutoSlTp/Fill)` | Может попасть в `snapshot.RecentLogs` (топ-15) — но при шторме fills может быть **вытеснено** |

⚠️ Для надёжного экспорта в External Brain `Hermes.Wpf` должен:
1. Слушать `TradingPlatformBridgeService.SnapshotUpdated`,
2. Диффить `Pnl.Today`, `Account.Equity`, `Positions`, `Orders`, `Strategies`, `Risk.EmergencyHalt` между обновлениями,
3. **Или** напрямую читать `%LocalAppData%/HermesTrading/trade_journal.jsonl` через файловый watcher для получения raw fills.

## 4.6. Ограничения paper-режима (явно отмеченные)

- **Нет maker-комиссии** — все fills облагаются taker-fee.
- **Нет partial fills для Limit** — заполняется на всю Quantity в одной транзакции.
- **Нет funding rate** для perpetual фьючерсов.
- **Нет insurance fund / liquidation engine** — `LiquidationPrice` указан в seed, но не пересчитывается.
- **Нет mark vs index price разделения** — `MarkPrice` всегда равен последнему `MarketTickEvent.Price`.
- **Slippage детерминирован**: `±0.0002` для Market. Реальная книга ордеров не симулируется.
- **Margin/FreeMargin** в `TradingAccount` не пересчитывается после fill — остаётся seed-значением. Это **давний пробел** (см. Findings).
