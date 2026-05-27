# 02. Доменная модель

Все доменные типы находятся в `Hermes.TradingPlatform.Core/Domain/` и видны всем backend-сервисам. UI работает через DTO из `Hermes.TradingPlatform.Shared/Mock/TradingMockModels.cs` (мапинг — `Hermes.TradingPlatform.Core/Mapping/TradingUiMapper.cs`).

## 2.1. Корневой агрегат — `TradingPlatformState`

`Domain/TradingPlatformState.cs`:

```3:15:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/TradingPlatformState.cs
public sealed class TradingPlatformState
{
    public TradingAccount Account { get; set; } = new();
    public PnlTracker Pnl { get; set; } = new();
    public RiskProfile Risk { get; set; } = new();
    public HermesState Hermes { get; set; } = new();
    public List<Position> Positions { get; } = [];
    public List<Order> Orders { get; } = [];
    public List<MarketTicker> Tickers { get; } = [];
    public List<StrategyState> Strategies { get; } = [];
    public List<PlatformLogEntry> Logs { get; } = [];
    public List<TradeJournalEntry> Journal { get; } = [];
}
```

Это **единственное "живое" состояние" платформы**. Управляется `TradingStateStore` (см. ниже). Никаких бэкенд-баз кроме файлов — нет.

`TradingStateStore` (`Hermes.TradingPlatform.Data/TradingStateStore.cs`):
- Хранит экземпляр `_state` под `lock(_sync)`.
- `Snapshot` отдаёт **глубокий клон** (см. метод `Clone`), поэтому UI и команды никогда не работают с живым объектом.
- `Mutate(Action<TradingPlatformState> update)` — единственный путь изменения состояния, после вызова поднимает `StateChanged`.
- `Initialize / Replace` атомарно подменяют весь state (используется при загрузке сессии и сбросе аккаунта).

> ⚠️ Все мутации идут через `Mutate`, но **под локом исполняется любой переданный делегат**. Большие операции (например, добавление 100+ ордеров) удержат лок. На практике это не критично, но в нагрузочных сценариях может стать узким местом.

## 2.2. Аккаунт и PnL

### `TradingAccount`
```1:10:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/TradingAccount.cs
public sealed class TradingAccount
{
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal FreeMargin { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal Leverage { get; set; } = 1m;
}
```

- `Balance` — realized + начальный депозит.
- `Equity` — `Balance + Σ Position.UnrealizedPnl` (пересчитывается `TradingStateCalculator.RecalculateEquity` в `MarketTickProjection` и `FillOrder`).
- `FreeMargin` / `UsedMargin` — **в текущей реализации не пересчитываются после fills**; задаются seed-данными (`100k / 24k`) и затем остаются как есть (см. раздел "Findings").
- `Leverage` — рабочее плечо аккаунта (1–`Risk.MaxLeverage`). Применяется в `RiskValidator` для расчёта margin.

### `PnlTracker`
```1:10:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/PnlTracker.cs
public sealed class PnlTracker
{
    public decimal Today { get; set; }
    public decimal Week { get; set; }
    public decimal Month { get; set; }
    public decimal AllTime { get; set; }
}
```

Все четыре поля обновляются **одним и тем же realized PnL** при каждом fill (см. `VirtualExchangeEngine.FillOrder`). Откатов по календарю (сброс `Today` в 00:00 UTC) **нет** — это известное ограничение paper-режима.

### `LeverageMode`
```1:8:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/LeverageMode.cs
public enum LeverageMode
{
    Fixed,
    Maximum,
}
```

Хранится как строка в `PlatformSettingsDto.LeverageMode`. `Fixed` → используется заданное `AccountLeverage`, ограниченное `Risk.MaxLeverage`. `Maximum` → берёт `Risk.MaxLeverage` (см. `TradingPlatformHost.ResolveEffectiveLeverage`).

## 2.3. Позиции и ордера

### `Position`
```1:14:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/Position.cs
public sealed class Position
{
    public required string Symbol { get; init; }
    public PositionSide Side { get; set; }
    public decimal Size { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal? LiquidationPrice { get; set; }
}
```

- **Hedge-mode по дизайну**: на каждый `Symbol` может одновременно быть `PositionSide.Long` И `PositionSide.Short` (см. `VirtualExchangeEngine.ApplyFillToPosition`: ищется позиция по `(Symbol, openSide)`).
- `Size` — единицы базового актива (BTC, ETH, ...).
- `EntryPrice` пересчитывается **взвешенным средним** при `Add` (см. `ApplyFillToPosition`):
  `EntryPrice = (EntryOld * SizeOld + FillPrice * Qty) / (SizeOld + Qty)`.
- `LiquidationPrice` задаётся только seed-данными — **в реальном времени не пересчитывается** (известное ограничение).

### `Order`
```1:15:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/Order.cs
public sealed class Order
{
    public required string Id { get; init; }
    public required string Symbol { get; init; }
    public OrderType Type { get; set; }
    public OrderSide Side { get; set; }
    public decimal Price { get; set; }
    public decimal? TriggerPrice { get; set; }
    public decimal Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public bool ReduceOnly { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- `Id` — формат `o-<seq>`, где `seq` стартует с 1003 и инкрементируется атомарно (`Interlocked.Increment`).
- `Price` после fill **переписывается** на фактический fill-price (включая slippage для Market). Это значит, что после `Status=Filled` исходная цена лимит-ордера в `Price` уже не видна.
- `TriggerPrice` используется только для `OrderType.Stop` (если не задано — fallback на `Price`).

### `TradingEnums.cs`
```1:52:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/TradingEnums.cs
public enum PositionSide { Long, Short, }
public enum OrderSide { Buy, Sell, }
public enum OrderType { Market, Limit, Stop, }
public enum OrderStatus { Open, Filled, Cancelled, Rejected, }
public enum StrategyRunStatus { Idle, Running, Halted, }
public enum HermesOrchestrationState { Offline, Monitoring, Reviewing, Halted, }
public enum RiskLevel { Low, Medium, High, Critical, }
```

Маппинг между ордерами и SL/TP делается **по соглашению** в `TradingUiMapper.ToDto(Order)`:

| Order | ReduceOnly | Type | Purpose |
|---|---|---|---|
| Stop, ReduceOnly | true | Stop | "SL" |
| Limit, ReduceOnly | true | Limit | "TP" |
| Market/Limit ReduceOnly | true | * | "Reduce" |
| Любой другой | false | * | "Entry" |

`PositionDto.StopLossPrice / TakeProfitPrice` подтягиваются из открытых reduce-only ордеров с противоположной стороной:

```28:48:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Mapping/TradingUiMapper.cs
    public static PositionDto ToDto(Position position, IReadOnlyList<Order> orders)
    {
        var exitSide = position.Side == PositionSide.Long ? OrderSide.Sell : OrderSide.Buy;
        decimal? sl = null;
        decimal? tp = null;
        foreach (var o in orders)
        {
            if (o.Status != OrderStatus.Open || !o.ReduceOnly || o.Side != exitSide || o.Symbol != position.Symbol)
                continue;

            if (o.Type == OrderType.Stop && sl is null)
                sl = o.TriggerPrice ?? o.Price;
            else if (o.Type == OrderType.Limit && tp is null)
                tp = o.Price;
        }
```

Берётся **первый встретившийся** SL и TP — если их несколько на одну позицию, в UI попадёт только один.

## 2.4. Журнал сделок — `TradeJournalEntry`

```1:20:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/TradeJournalEntry.cs
public sealed class TradeJournalEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string OrderId { get; init; }
    public required string Symbol { get; init; }
    /// <summary>Open | Add | Reduce | Close</summary>
    public required string Kind { get; init; }
    public required string Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal FillPrice { get; init; }
    public decimal Fee { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal BalanceBefore { get; init; }
    public decimal BalanceAfter { get; init; }
    public bool ReduceOnly { get; init; }
}
```

Это **единственная per-fill единица экономики**. Каждая запись добавляется `TradeJournalProjection.OnOrderFilled` в `state.Journal` (с лимитом 500 топовых) и одновременно в `IJournalStore` (`TradeJournalFileWriter` или `SqliteJournalStore`).

### `Kind` — детерминирован `PositionFillImpact`

```1:3:Hermes.TradingPlatform/Hermes.TradingPlatform.Exchange/PositionFillImpact.cs
internal readonly record struct PositionFillImpact(decimal RealizedPnl, string JournalKind);
```

| Сценарий | Kind |
|---|---|
| Открытие новой позиции (нет существующей по `(Symbol, openSide)`) | `Open` |
| Увеличение существующей позиции (одной стороны) | `Add` |
| Reduce-only, **частично** закрывает позицию | `Reduce` |
| Reduce-only, закрывает позицию полностью (`closedQty ≥ sizeBefore`) | `Close` |

## 2.5. Риск-профиль

`RiskProfile.cs` имеет дуальный характер: одновременно **конфигурация** и **текущие метрики**:

```16:36:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/RiskProfile.cs
public sealed class RiskProfile
{
    // Конфигурация (редактируется в Risk Manager):
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public decimal MaxRiskPerTradePercent { get; set; } = 1m;
    public decimal MaxPositionSizeBtc { get; set; } = 0.5m;
    public decimal MaxLeverage { get; set; } = 5m;
    public decimal MaxExposurePercent { get; set; } = 50m;
    public decimal DefaultTakeProfitRrMultiplier { get; set; } = 2m;
    public bool AutoApplyDefaultSlTp { get; set; } = true;
    public bool SafeMode { get; set; } = true;
    public bool AutoShutdown { get; set; } = true;
    public bool EmergencyHalt { get; set; }

    // Live-метрики (обновляются циркулярным брейкером):
    public decimal DailyDrawdownPercent { get; set; }
    public decimal ExposurePercent { get; set; }
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
}
```

AUDIT-комментарий в начале файла фиксирует, какие поля попадают в `bridge/snapshot.json.RiskSnapshot`:

- **Surface**: `RiskLevel`, `DailyDrawdownPercent`, `ExposurePercent`, `SafeMode`, `EmergencyHalt`, `MaxLeverage`.
- **NOT surface**: `MaxDailyLossPercent`, `MaxRiskPerTradePercent`, `MaxPositionSizeBtc`, `MaxExposurePercent`, `DefaultTakeProfitRrMultiplier`, `AutoApplyDefaultSlTp`, `AutoShutdown`.

`DailyDrawdownPercent` и `ExposurePercent` в текущей реализации **обновляются только seed-данными** — в коде не нашлось места, где они пересчитываются на каждый fill/tick. Это **значимый пробел** (см. `10_Findings_And_Recommendations.md`, секция 10.2).

## 2.6. Hermes state — `HermesState`

```3:14:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/HermesState.cs
public sealed class HermesState
{
    public HermesOrchestrationState State { get; set; } = HermesOrchestrationState.Monitoring;
    public string ActiveStrategy { get; set; } = "";
    public decimal Confidence { get; set; }
    public string Mode { get; set; } = "Orchestration / Paper";
    public string CurrentReasoning { get; set; } = "";
    public string StrategyContext { get; set; } = "";
    public List<HermesTask> Tasks { get; } = [];
    public List<HermesDecision> Decisions { get; } = [];
}
```

`Decisions` ограничен 50 записями (см. `HermesOrchestrationService.MaxDecisions`), `Tasks` — 20.

`Confidence` вычисляется `HermesOrchestrationService.RefreshReasoning` как:

```
confidence = Clamp(1 - DailyDrawdownPercent / 20, 0.25, 0.95)
```

То есть прямо привязан к live-drawdown.

## 2.7. Стратегии — `StrategyState` / `StrategyParameters`

```3:11:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/StrategyState.cs
public sealed class StrategyState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskProfileLabel { get; init; }
    public StrategyRunStatus Status { get; set; }
    public bool IsEnabled { get; set; }
}
```

`StrategyState` лежит в `state.Strategies` и описывает UI-карточку стратегии. Объект стратегии (`ITradingStrategy`) живёт **отдельно** в `TradingPlatformHost._strategiesById` — между ними нет прямого ref'a, синхронизация по `Id`.

```8:20:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/StrategyParameters.cs
public sealed class StrategyParameters
{
    public required string StrategyId { get; init; }
    public decimal Quantity { get; set; }
    public decimal ChangeThresholdPercent { get; set; }
    public int CooldownSeconds { get; set; }
}
```

Параметры **hot-reloadable**: при `UpdateStrategyParameters` они и сохраняются в `strategy-parameters.json`, и применяются к рантайм-инстансу стратегии через `ApplyParameters`.

## 2.8. Рыночные данные — `MarketTicker` + `MarketFeedStatus`

```3:10:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/MarketTicker.cs
public sealed class MarketTicker
{
    public required string Symbol { get; init; }
    public decimal Price { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Volume24h { get; set; }
    public bool InWatchlist { get; set; }
}
```

```3:16:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/MarketFeedStatus.cs
public enum MarketFeedStatus { Stopped, Connecting, Connected, Reconnecting, Error, }
public enum MarketDataSource { Mock, BinanceFutures, }
```

## 2.9. Логи — `PlatformLogEntry`

```3:9:Hermes.TradingPlatform/Hermes.TradingPlatform.Core/Domain/PlatformLogEntry.cs
public sealed class PlatformLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public required string EventType { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
}
```

Список ограничен 200 записями (`EventLogProjection`). UI берёт их через `TradingReadModel.GetLogs()` → `LogsViewModel`.

Внутри `EventType` используются строки: `"Order"`, `"Fill"`, `"Risk"`, `"Strategy"`, `"Position"`, `"Hermes"`, `"System"`, `"AutoSlTp"`, `"Market"`. Это **не enum** — поиск по логам в UI идёт по строковому совпадению.

## 2.10. Сводная карта моделей

```
TradingPlatformState (root)
├── TradingAccount      ← Balance, Equity, FreeMargin*, UsedMargin*, Leverage
├── PnlTracker          ← Today, Week, Month, AllTime
├── RiskProfile         ← config + DailyDrawdownPercent*, ExposurePercent*, RiskLevel
├── HermesState         ← rule-based observer state
│   ├── List<HermesTask>
│   └── List<HermesDecision>
├── List<Position>      ← hedge-mode (Long+Short на 1 symbol)
├── List<Order>         ← Market/Limit/Stop + ReduceOnly + статус
├── List<MarketTicker>  ← обновляются MarketTickProjection
├── List<StrategyState> ← UI-карточки
├── List<PlatformLogEntry>  ← cap 200 (новые в head)
└── List<TradeJournalEntry> ← cap 500 (новые в head)

(*) поле НЕ пересчитывается полностью в текущей реализации
```
