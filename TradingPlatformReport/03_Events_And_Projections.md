# 03. События и проекции

## 3.1. Событийная шина — `IEventBus` / `InMemoryEventBus`

Контракт (`Hermes.TradingPlatform.Core/Events/IEventBus.cs`):

```3:9:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Events/IEventBus.cs
public interface IEventBus
{
    void Subscribe<T>(Action<T> handler) where T : class, IPlatformEvent;
    void SubscribeAll(Action<IPlatformEvent> handler);
    void Publish<T>(T platformEvent) where T : class, IPlatformEvent;
}
```

Реализация — `InMemoryEventBus` (`Core/Events/InMemoryEventBus.cs`):

- Хранит словарь `Type → List<Delegate>` и список глобальных подписчиков под одним `lock`.
- `Publish` копирует список подписчиков в массив **до** вызова, поэтому подписки/отписки **во время** диспетча не блокируют публикацию. Это типовая lock-and-snapshot стратегия.
- Каждый обработчик вызывается в **try/catch**, ошибки логируются через `OnHandlerError` (по умолчанию `Trace.WriteLine`). Это важно: исключение в одном подписчике **не ломает** остальных.
- Диспетч **синхронный** в потоке `Publish` — нет очереди, нет `Task.Run`. Это сознательно: проекции должны атомарно изменять состояние в одной транзакции с публикатором.

> ⚠️ Глобальные обработчики (`SubscribeAll`) вызываются **после** типизированных. Если глобальный обработчик мутирует state, эффект может быть незаметен типизированному.

Базовый тип события — `PlatformEventBase`:

```3:8:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Events/PlatformEventBase.cs
public abstract record PlatformEventBase : IPlatformEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public abstract string EventType { get; }
}
```

`OccurredAt` фиксируется в момент создания экземпляра события (`record` immutable). Все события — `sealed record`.

## 3.2. Каталог событий

### 3.2.1. Рыночные (`Core/Events/MarketStreamEvents.cs` + `TradingEvents.cs`)

| Событие | Источник | EventType | Подписчики |
|---|---|---|---|
| `MarketTickEvent(Symbol, Price, Bid, Ask, ChangePercent24h?, QuoteVolume24h?)` | `BinanceFuturesMarketDataFeed.PublishOne24hrStatAsync` + WS-парсер, либо `MockMarketDataFeed.PublishTicks` | `MarketTick` | `MarketTickProjection`, `VirtualExchangeEngine.OnMarketTick` (для заполнения Limit/Stop), `StrategyRunner.OnMarketTick`, `HermesOrchestrationService.OnMarketTick` (refresh каждые 20 с), `RiskCircuitBreaker.Evaluate` |
| `MarketTradeEvent` (aggTrade) | подготовлено в `BinanceFuturesStreamParser`, но WS-стрим на `@ticker` его не публикует | `MarketTrade` | — (не используется) |
| `MarketKlineEvent` (kline candles) | объявлен, но **не публикуется** в текущей реализации | `MarketKline` | — |

### 3.2.2. Торговые (`Core/Events/TradingEvents.cs`)

| Событие | Источник | EventType | Подписчики |
|---|---|---|---|
| `OrderPlacedEvent(Order)` | `VirtualExchangeEngine.PlaceOrder` (только если risk allowed) | `OrderPlaced` | `EventLogProjection` (→ "Order o-XXXX placed"), `TradingStatePersistence.ScheduleSave`, `HermesOrchestrationService` (только Rejected → decision) |
| `OrderFilledEvent(Order, FillPrice, Fee, RealizedPnl, BalanceBefore, BalanceAfter, JournalKind)` | `VirtualExchangeEngine.FillOrder` | `OrderFilled` | `EventLogProjection` (детальный лог), `TradeJournalProjection` (→ state.Journal + IJournalStore), `TradingStatePersistence.SaveNow` (немедленный сейв), `TradingSoundService` (звук в зависимости от `JournalKind`), `HermesOrchestrationService.OnOrderFilled` (decision-постфактум), `RiskCircuitBreaker.Evaluate` |
| `OrderCancelledEvent(OrderId, Symbol)` | `VirtualExchangeEngine.TryCancelOrder` | `OrderCancelled` | `EventLogProjection`, `TradingStatePersistence.ScheduleSave` |
| `PositionClosedEvent(Symbol, RealizedPnl)` | `VirtualExchangeEngine.FillOrder` (если `realized != 0 && journalKind == "Close"`) | `PositionClosed` | `EventLogProjection` |
| `RiskTriggeredEvent(Reason, EmergencyHalt)` | `RiskCircuitBreaker.Evaluate` или `TradingPlatformHost.EmergencyStop` | `RiskTriggered` | `EventLogProjection` (EventType=`Risk`), `HermesOrchestrationService.OnRiskTriggered` (Hermes.State → Halted + decision + task) |
| `StrategySignalEvent(StrategyId, StrategyName, Symbol, Side, OrderType, Quantity, Price, Reason, AutoExecuteRequested)` | `StrategyRunner.OnMarketTick` | `StrategySignal` | `EventLogProjection`, `HermesOrchestrationService.OnStrategySignal` (Hermes.State → Reviewing + decision) |
| `PlatformLogEvent(PlatformLogEntry)` | Любой код, который хочет добавить лог напрямую: `VirtualExchangeEngine`, `RiskCircuitBreaker`, `TradingPlatformHost`, `HermesOrchestrationService` | `Log` | `EventLogProjection` (`SubscribeAll` пропускает `PlatformLogEvent` — иначе была бы рекурсия) |

### 3.2.3. Особенности диспетча

- Когда `VirtualExchangeEngine.PlaceOrder` создаёт `Market` ордер, в одной публикации лезет цепочка `OrderPlacedEvent → FillOrder → OrderFilledEvent → PositionClosedEvent? → PlatformLogEvent` — всё в синхронном стеке. Это означает, что **подписчики `OrderFilled` обязаны быть быстрыми**, иначе клиентский поток UI (через `WpfThreading.RunOnUi`, см. ниже) ощутит лаг.
- `WpfThreading.RunOnUi` (см. `Threading/WpfThreading.cs`) — обёртка над `Dispatcher.BeginInvoke`, используется в ViewModels для безопасной модификации `ObservableCollection` из background-фидов.

## 3.3. Проекции

Проекции — это сервисы из `Hermes.TradingPlatform.Data/Projections/`, которые **подписываются на события и мутируют state**. Они конструируются в `TradingPlatformHost.ctor` и удерживаются через `_ = new ...` — то есть подписка на шину сама по себе удерживает их живыми (`InMemoryEventBus` хранит делегаты).

### 3.3.1. `MarketTickProjection`

`Data/Projections/MarketTickProjection.cs`. Подписан на `MarketTickEvent`.

Что делает на каждый тик:

```19:48:Hermes.TradingPlatform/Hermes.TradingPlatform.Data/Projections/MarketTickProjection.cs
    private void OnTick(MarketTickEvent tick)
    {
        _store.Mutate(state =>
        {
            var ticker = state.Tickers.FirstOrDefault(t => t.Symbol == tick.Symbol);
            if (ticker is not null)
            {
                ticker.Price = tick.Price;
                if (tick.ChangePercent24h.HasValue) { ticker.ChangePercent24h = tick.ChangePercent24h.Value; }
                if (tick.QuoteVolume24h.HasValue)   { ticker.Volume24h        = tick.QuoteVolume24h.Value; }
            }

            foreach (var position in state.Positions.Where(p => p.Symbol == tick.Symbol))
            {
                position.MarkPrice = tick.Price;
                var direction = position.Side == PositionSide.Long ? 1m : -1m;
                position.UnrealizedPnl = (tick.Price - position.EntryPrice) * position.Size * direction;
            }

            TradingStateCalculator.RecalculateEquity(state);
        });
    }
```

Ключевые свойства:
- Обновляет тикер только если он уже в `state.Tickers` (новые символы не добавляет — их даёт seed/restore).
- `UnrealizedPnl` пересчитывается для **всех позиций по этому символу** (включая обе стороны при hedge).
- `Equity` = `Balance + Σ UnrealizedPnl` пересчитывается в каждом тике — это значит, что в UI equity дёргается даже без сделок.

### 3.3.2. `EventLogProjection`

`Data/Projections/EventLogProjection.cs`. Подписан на **все** события через `SubscribeAll`, фильтрует:

- `PlatformLogEvent` → **пропускается** (его обработка идёт через прямой вызов `_bus.Publish(PlatformLogEvent(...))` из других мест; иначе была бы рекурсия).
- `MarketTickEvent` → пропускается (live Binance даёт высокую частоту — иначе лог переполнялся бы).
- Остальные события сериализуются в текстовый лог:

| Тип | Source | Сообщение |
|---|---|---|
| `OrderPlacedEvent` | `VirtualExchange` | `Order o-XXXX placed (Symbol Side Qty)` |
| `OrderFilledEvent` | `VirtualExchange` | `Order o-XXXX filled @ Price (Kind) realized=... fee=... balance ...→...` |
| `OrderCancelledEvent` | `VirtualExchange` | `Order o-XXXX cancelled (Symbol)` |
| `PositionClosedEvent` | `VirtualExchange` | `Position closed Symbol realized=...` |
| `RiskTriggeredEvent` | `RiskManager` | reason as-is |
| `StrategySignalEvent` | StrategyName | `Symbol Side OrderType qty X — reason` |

Все добавляются в `state.Logs.Insert(0, ...)`, при превышении 200 записей хвост обрезается. То есть лог — **LIFO**, новые сверху.

### 3.3.3. `TradeJournalProjection`

`Data/Projections/TradeJournalProjection.cs`. Подписан только на `OrderFilledEvent`.

На каждый fill:

1. Конструирует `TradeJournalEntry` (timestamp = `filled.OccurredAt`, остальные поля из события + `order.ReduceOnly`).
2. Вставляет в `state.Journal[0]`, обрезает хвост при ≥ 500 записей.
3. Вызывает `_journalStore.Append(entry)` (см. JSON / SQLite в `07_Persistence_And_Journal.md`).

> ⚠️ Если `IJournalStore.Append` бросит исключение — оно проскочит через `InMemoryEventBus.OnHandlerError` и засветится только в `Trace`. State будет обновлён, а файл — нет. На практике это маловероятно (lock + AppendAllText), но возможно при ошибках I/O.

## 3.4. Подписки `VirtualExchangeEngine` и `StrategyRunner` на тики

Кроме проекций есть ещё два важных подписчика `MarketTickEvent`:

### `VirtualExchangeEngine.OnMarketTick`
Перебирает все открытые ордера по символу тика, для каждого проверяет условие исполнения (`Limit ≤ priceCondition`, `Stop ≥/≤ trigger`). Если ордер должен исполниться — вызывает `FillOrder(id, marketPrice)`.

Это означает, что **fill происходит в потоке market feed** (либо в потоке таймера mock-feed) — не в UI-потоке. Все мутации идут через `TradingStateStore.Mutate` (lock), поэтому это безопасно.

### `StrategyRunner.OnMarketTick`
Если `Risk.EmergencyHalt = false`, перебирает все стратегии, у каждой вызывает `Evaluate(tick, snapshot)`. Если возвращён `StrategySignal`:

- Публикует `StrategySignalEvent` (для EventLog + HermesOrchestration).
- Если `signal.AutoExecute && AutoExecuteEnabled` — вызывает `_exchange.PlaceOrder(...)`. То есть **исполнение стратегии идёт в том же потоке**, что и market-tick.

## 3.5. Диаграмма потока событий

```
            ┌──────────────────────────────────────────────────┐
            │            InMemoryEventBus (sync)               │
            └──┬──────┬───────────┬───────────┬────────────┬──┘
               │      │           │           │            │
   MarketTick  │      │ Strategy  │  Order*   │  Position  │ Risk*
               │      │  Signal   │           │  Closed    │
               ▼      ▼           ▼           ▼            ▼
    ┌────────────┐ ┌──────────┐ ┌───────────┐ ┌──────────┐ ┌──────────┐
    │ MarketTick │ │ EventLog │ │ TradeJ.   │ │ EventLog │ │ EventLog │
    │ Projection │ │ Proj.    │ │ Proj.     │ │ Proj.    │ │ Proj.    │
    │            │ │          │ │ + Journal │ │          │ │          │
    │ → tickers, │ │ → state. │ │   Store   │ │ → state. │ │ → state. │
    │   positions│ │   Logs   │ │ (jsonl/db)│ │   Logs   │ │   Logs   │
    │   uPnl,    │ └──────────┘ └───────────┘ └──────────┘ └──────────┘
    │   equity   │
    └─────┬──────┘
          │
    ┌─────▼────────┐  ┌──────────────┐  ┌────────────────┐  ┌────────────────┐
    │ VirtualExch. │  │ StrategyRunr │  │ HermesOrchSrv  │  │ RiskCircuitBr. │
    │ (try fill    │  │ (eval all,   │  │ (update reason │  │ (auto-shutdown │
    │  open Limit/ │  │  publish     │  │  every 20s)    │  │  if dailyloss/ │
    │  Stop)       │  │  signal,     │  │                │  │  exposure cap) │
    └──────────────┘  │  optionally  │  └────────────────┘  └────────────────┘
                      │  PlaceOrder) │
                      └──────────────┘
```

## 3.6. Что **не** генерируется в текущем коде

- **OrderModifiedEvent** — нет; modify реализован как cancel + place, поэтому в логе видны 2 события (Cancel + Placed) + строковый "Modify ... → ..." из `PlatformLogEvent`. Если потребитель хочет видеть пару "до/после" в одном событии — нужно ввести новое событие.
- **AccountUpdatedEvent / PnlSnapshotEvent** — баланс/PnL меняются только через `OrderFilledEvent`. Внешние снимки делаются через `StateChanged` (см. ниже).
- **Generic "StateChanged" event на шине** — отсутствует. Вместо этого `TradingStateStore` поднимает простой `event EventHandler StateChanged` (не через bus). `TradingReadModel` ретранслирует его для UI, `TradingBridgePublisher` — для publish snapshot.
- **MarketKlineEvent / MarketTradeEvent** — типы есть, но `BinanceFuturesMarketDataFeed` не публикует их. Стратегии работают только с `MarketTickEvent`.

## 3.7. Гарантии порядка

- **Внутри одного fill** последовательность гарантирована: сначала state-мутация под локом, затем `OrderFilledEvent`, затем `PlatformLogEvent("Fill")`, затем (если close) `PositionClosedEvent`, затем `TryAttachDefaultSlTp` (который сам делает `PlaceOrder` → возможна цепочка `OrderPlacedEvent`).
- **Между разными fills** порядок зависит от потока, который вызвал `PlaceOrder`. UI-команды идут на UI-потоке (через `Dispatcher`), market-fed fills — на потоке фида. Лок в `TradingStateStore` сериализует state-мутации, но **не гарантирует FIFO между потоками**.
- **Внешние команды через bridge** проходят `WpfThreading.RunOnUi`, то есть исполняются на UI-потоке (`TradingBridgeCommandProcessor.Execute`).
