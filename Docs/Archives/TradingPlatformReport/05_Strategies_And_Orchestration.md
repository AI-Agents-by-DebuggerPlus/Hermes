# 05. Стратегии и Hermes-оркестратор

## 5.1. Контракт `ITradingStrategy`

`Hermes.TradingPlatform.Core/Abstractions/ITradingStrategy.cs`:

```6:38:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Abstractions/ITradingStrategy.cs
public interface ITradingStrategy
{
    string Id { get; }
    string Name { get; }
    StrategySignal? Evaluate(MarketTickEvent tick, TradingPlatformState state);

    StrategyParameters DefaultParameters => new() {
        StrategyId = Id, Quantity = 0.01m, ChangeThresholdPercent = 0.5m, CooldownSeconds = 60,
    };

    void ApplyParameters(StrategyParameters parameters) { /* no-op */ }
}

public sealed class StrategySignal
{
    public required string Symbol { get; init; }
    public OrderSide Side { get; init; }
    public OrderType OrderType { get; init; } = OrderType.Market;
    public decimal Quantity { get; init; }
    public decimal Price { get; init; }
    public required string Reason { get; init; }
    public bool AutoExecute { get; init; } = true;
}
```

- Стратегия **stateless по контракту** — каждый Evaluate получает свежий snapshot. Внутренне может хранить cooldown / последние тики.
- `Evaluate` возвращает `null` — нет сигнала; либо `StrategySignal` — сигнал готов к отправке.
- `AutoExecute=true` — runner вызовет `PlaceOrder`. `false` — только опубликует событие, не разместит ордер.
- `ApplyParameters` — default no-op; кастомные стратегии могут читать `Quantity / ChangeThresholdPercent / CooldownSeconds`.

## 5.2. `StrategyRunner`

Файл: `Hermes.TradingPlatform.Strategies/StrategyRunner.cs`. Подписан на `MarketTickEvent`.

```30:79:Hermes.TradingPlatform/Hermes.TradingPlatform.Strategies/StrategyRunner.cs
    private void OnMarketTick(MarketTickEvent tick)
    {
        var snapshot = _store.Snapshot;
        if (snapshot.Risk.EmergencyHalt) return;

        foreach (var strategy in _strategies)
        {
            var signal = strategy.Evaluate(tick, snapshot);
            if (signal is null) continue;

            _bus.Publish(new StrategySignalEvent(
                strategy.Id, strategy.Name, signal.Symbol, signal.Side,
                signal.OrderType, signal.Quantity, signal.Price, signal.Reason,
                signal.AutoExecute && AutoExecuteEnabled));

            if (!signal.AutoExecute || !AutoExecuteEnabled) continue;

            var order = _exchange.PlaceOrder(signal.Symbol, signal.OrderType, signal.Side,
                signal.Quantity, signal.Price, reduceOnly: false);

            _store.Mutate(s => {
                var card = s.Strategies.FirstOrDefault(x => x.Id == strategy.Id);
                if (card is not null && order.Status != OrderStatus.Rejected)
                    card.Status = StrategyRunStatus.Running;
            });
        }
    }
```

**Свойства**:
- `AutoExecuteEnabled` (default `true`) — глобальный kill-switch, не привязан к UI. (`Strategies` page управляет per-strategy `IsEnabled`, но runner всегда вызывает `Evaluate` — стратегия сама проверяет `IsEnabled` в state.)
- При `EmergencyHalt` runner **не** обращается к стратегиям (даже `Evaluate` не вызывается).
- Каждая стратегия запускается **последовательно** в потоке тика; нет parallelism.
- Если ордер успешно размещён, UI-карточка стратегии переводится в `Running`. Но это только индикатор — статус не сбрасывается в `Idle` после ордера.

## 5.3. `StrategyCooldown`

`Hermes.TradingPlatform.Strategies/StrategyCooldown.cs` — простой rate-limiter:

```16:25:Hermes.TradingPlatform/Hermes.TradingPlatform.Strategies/StrategyCooldown.cs
    public bool TryAcquire()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSignalAt < _interval) return false;
        _lastSignalAt = now;
        return true;
    }
```

Не потокобезопасный по записи — на практике вызывается только в потоке тика, так что ОК.

## 5.4. Встроенные стратегии

Все три стратегии работают на **24h % change** из `MarketTickEvent` и фильтруют по **одному символу**. Это MVP-симуляция, не production-стратегии.

### 5.4.1. `MomentumStrategy` (`momentum`)

Файл: `Strategies/BuiltIn/MomentumStrategy.cs`.

- Символ: **BTCUSDT** (захардкожен).
- Defaults: `Quantity=0.01`, `ChangeThresholdPercent=0.6`, `CooldownSeconds=45`.
- Условие: `change > +0.6%` → **Long Market** на `tick.Price`.
- Risk profile: `"Aggressive"`.

### 5.4.2. `MeanReversionStrategy` (`mean-rev`)

Файл: `Strategies/BuiltIn/MeanReversionStrategy.cs`.

- Символ: **ETHUSDT**.
- Defaults: `Quantity=0.05`, `ChangeThresholdPercent=0.8`, `CooldownSeconds=60`.
- Условие:
  - `change < −0.8%` → **Long Market** (fade down).
  - `change > +0.8%` → **Short Market** (fade up).
- Risk profile: `"Conservative"`.

### 5.4.3. `LiquiditySweepStrategy` (`liq-sweep`)

Файл: `Strategies/BuiltIn/LiquiditySweepStrategy.cs`.

- Символ: **SOLUSDT**.
- Defaults: `Quantity=5`, `ChangeThresholdPercent=2.0`, `CooldownSeconds=90`.
- Условие: `|change| ≥ 2%`. Если `change > 0` → Sell Limit на `price * 1.002`; иначе Buy Limit на `price * 0.998`.
- Risk profile: `"Moderate"`.

> ⚠️ Все три стратегии **жёстко привязаны к конкретному символу**. Multi-symbol поддержка потребует доработки.
> Включение/отключение в UI делается через `state.Strategies[i].IsEnabled` — стратегия сама проверяет это в `IsEnabled(state)` лямбде.

### 5.4.4. Параметры стратегий

Каждая стратегия читает `_quantity / _changeThreshold / _cooldown.Interval` и применяет персистированные значения через `ApplyParameters`. Если параметр ≤ 0 — игнорируется (остаётся default).

В UI карточек стратегий (`StrategiesViewModel + StrategyParametersDialog`) пользователь вводит новые значения; `TradingPlatformHost.UpdateStrategyParameters` сохраняет их в `strategy-parameters.json` и применяет к runtime-инстансу. **Hot-reload** на следующем тике.

## 5.5. `HermesOrchestrationService`

Файл: `Hermes.TradingPlatform.Orchestration/HermesOrchestrationService.cs`. Реализует `IHermesOrchestrator`. Это **rule-based observer**, который:

- **Никогда не размещает ордера** (это правило, явно прописано в комментариях и в самом коде).
- Обновляет `state.Hermes.{State, ActiveStrategy, Confidence, Mode, CurrentReasoning, StrategyContext, Decisions, Tasks}` для UI Hermes-вкладки.

### 5.5.1. Подписки

```20:30:Hermes.TradingPlatform/Hermes.TradingPlatform.Orchestration/HermesOrchestrationService.cs
    public HermesOrchestrationService(IEventBus bus, ITradingStateStore store)
    {
        _bus.Subscribe<MarketTickEvent>(OnMarketTick);
        _bus.Subscribe<StrategySignalEvent>(OnStrategySignal);
        _bus.Subscribe<RiskTriggeredEvent>(OnRiskTriggered);
        _bus.Subscribe<OrderFilledEvent>(OnOrderFilled);
        _bus.Subscribe<OrderPlacedEvent>(OnOrderPlaced);
    }
```

### 5.5.2. Поведение по событиям

| Событие | Действие |
|---|---|
| `MarketTickEvent` | Раз в 20 секунд (`_lastReasoningRefresh`) обновляет `CurrentReasoning`, `StrategyContext`, `Confidence`, `State` |
| `StrategySignalEvent` | Добавляет в `Decisions`: "Review [Name]: side symbol type — reason. {exec_note}". `state.Hermes.State = Reviewing`. Сразу вызывает `RefreshReasoning`. |
| `RiskTriggeredEvent` | `state.Hermes.State = Halted`, `Mode = "Halted — operator action required"`. Добавляет decision + task "Review emergency halt". |
| `OrderFilledEvent` | Добавляет decision "Post-trade review: o-XXX symbol filled @ ..." |
| `OrderPlacedEvent` | Только при `Status == Rejected` — decision "Order rejected: ..." |

### 5.5.3. `RefreshReasoning`

```125:177:Hermes.TradingPlatform/Hermes.TradingPlatform.Orchestration/HermesOrchestrationService.cs
    private void RefreshReasoning(TradingPlatformState snapshot)
    {
        var running = snapshot.Strategies.Where(s => s is { IsEnabled: true, Status: StrategyRunStatus.Running }).Select(s => s.Name).ToList();
        var active = running.FirstOrDefault() ?? "—";
        var positionSummary = snapshot.Positions.Count == 0 ? "flat" : string.Join(", ", ...);

        var reasoning = $"Orchestration monitor: {snapshot.Risk.RiskLevel} risk, DD {snapshot.Risk.DailyDrawdownPercent:F1}%, " +
            $"exposure {snapshot.Risk.ExposurePercent:F1}%. Positions: {positionSummary}. " +
            $"Running strategies: ... — Hermes does not execute orders ...";

        if (snapshot.Risk.EmergencyHalt) reasoning = "EMERGENCY HALT active. Hermes suspended recommendations ...";
        else if (snapshot.Risk.SafeMode) reasoning += " Safe Mode: new entries must be reduce-only.";

        var confidence = snapshot.Risk.EmergencyHalt ? 0.1m : Math.Clamp(1m - snapshot.Risk.DailyDrawdownPercent / 20m, 0.25m, 0.95m);
        var state = snapshot.Risk.EmergencyHalt ? HermesOrchestrationState.Halted : HermesOrchestrationState.Monitoring;

        _store.Mutate(s => { s.Hermes.State = state; s.Hermes.ActiveStrategy = active; s.Hermes.Confidence = confidence; ... });
    }
```

Возвращает в UI: текущий уровень риска, DD, exposure, открытые позиции, активные стратегии. Плюс `Confidence` = от DD (вне halted режима).

### 5.5.4. Capacity limits

- `MaxDecisions = 50` — лента решений усекается до 50 (новые сверху).
- `MaxTasks = 20` — задачи усекаются до 20.

`AddDecision` ставит decision в начало списка и **публикует** `PlatformLogEvent` (EventType=`"Hermes"`, Source=`"Orchestrator"`, message обрезается до 120 символов).

### 5.5.5. Управление включением

`SetEnabled(bool)` — переключает `_enabled`. Если выключен, **все** обработчики выходят немедленно (return). Текущее состояние сохраняется в `PlatformSettingsDto.HermesOrchestrationEnabled`.

UI-управление: `SettingsViewModel.HermesIntegrationEnabled` (галочка) → `TradingPlatformHost.SetHermesOrchestrationEnabled` → `HermesOrchestrationService.SetEnabled` + persist.

## 5.6. Жизненный цикл сигнала стратегии (end-to-end)

```
MarketTickEvent (BTCUSDT @ 95100, +0.8%)
    │
    ├─► VirtualExchangeEngine.OnMarketTick → no open Limit/Stop for BTC → noop
    ├─► MarketTickProjection → ticker.Price update + uPnL
    └─► StrategyRunner.OnMarketTick
            │
            └─► MomentumStrategy.Evaluate
                    │   tick.ChangePercent24h = 0.8% > 0.6%
                    │   _cooldown.TryAcquire() = true
                    └─► return StrategySignal { Symbol="BTCUSDT", Side=Buy, Type=Market, Qty=0.01, ... }
            │
            ├─► _bus.Publish(StrategySignalEvent(...))   ← попадает в:
            │       ├─► EventLogProjection  → state.Logs[0] (EventType=Strategy)
            │       └─► HermesOrchestrationService.OnStrategySignal
            │               └─► decision: "Review [Momentum]: Buy BTCUSDT Market — ..."
            │
            └─► _exchange.PlaceOrder(BTCUSDT, Market, Buy, 0.01, 95100, reduceOnly=false)
                    │
                    ├─► RiskValidator.ValidateNewOrder → allowed (assuming risk OK)
                    ├─► state.Orders.Insert(0, order { Status=Open })
                    ├─► _bus.Publish(OrderPlacedEvent)
                    ├─► FillOrder(o-1005, 95100*1.0002 = 95119.02)
                    │       ├─► state.Mutate: order.Status=Filled, balance -= fee, ApplyFillToPosition(=> Open),
                    │       │                 PnL today/week/month/all updated
                    │       ├─► _bus.Publish(OrderFilledEvent)
                    │       │       ├─► EventLogProjection → state.Logs (Order filled @ ...)
                    │       │       ├─► TradeJournalProjection → state.Journal + trade_journal.jsonl
                    │       │       ├─► TradingStatePersistence.SaveNow → session-state.json
                    │       │       ├─► TradingSoundService → SystemSounds.Beep
                    │       │       ├─► HermesOrchestrationService.OnOrderFilled → decision "Post-trade review: ..."
                    │       │       └─► RiskCircuitBreaker.Evaluate (after fill state)
                    │       ├─► _bus.Publish(PlatformLogEvent "Fill" ...)
                    │       └─► TryAttachDefaultSlTp → 2 more PlaceOrder calls (Stop SL + Limit TP, reduceOnly=true)
                    │
                    └─► state.Strategies[Momentum].Status = StrategyRunStatus.Running
```

То есть **один тик** может вызвать **до 7+ событий** на шине (signal, placed × 3, filled × 1, log × 4, etc) и **5+ файловых записей** (state autosave, trade journal append, snapshot republish). При reasonable частоте тиков на Binance это нормально, но при шторме (например, новостной всплеск) — может накопиться очередь.

## 5.7. Хермес-наблюдатель — что он НЕ делает

Из комментариев в коде (`HermesOrchestrationService.cs`) и архитектуры:

- ❌ **Не размещает ордера**. Ни через `_exchange`, ни через event-bus. (StrategyRunner размещает; Hermes только комментирует.)
- ❌ **Не блокирует ордера**. Это работа `RiskValidator` / `RiskCircuitBreaker`.
- ❌ **Не использует LLM**. Это rule-based observer. LLM-чат живёт **отдельно** в `MiniAssistantViewModel` (OpenRouter), который только читает `TradingInAppAssistantContextProvider.GetLiveContextSnapshot()` для контекста промпта.
- ❌ **Не модифицирует риск-настройки автоматически**. Только пишет в `Decisions` / `Tasks`.

Это значит, что **термин "Hermes" в платформе** означает rule-based наблюдателя для UI-таба `Hermes`, а **не** ИИ-агент из `Hermes.Wpf`. ИИ-агент общается с платформой через bridge (см. `08_Bridge_And_CLI.md`).
