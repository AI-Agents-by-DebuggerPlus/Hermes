# Hermes Trading Platform — Implementation Audit

**Дата отчёта:** 2026-05-26
**Версия платформы:** 0.6.0 (см. `Directory.Build.props`)
**Корень исходников:** `Hermes.TradingPlatform/`
**Solution:** `Hermes.TradingPlatform/Hermes.TradingPlatform.sln`

---

## Назначение отчёта

Этот отчёт фиксирует **актуальное состояние реализации** торговой платформы Hermes: какие проекты входят в решение, какие у них роли и зависимости, как устроена доменная модель, поток событий, виртуальная биржа, риск-движок, стратегии, оркестрация Hermes, рыночные фиды, персистентность и WPF-оболочка. Также описан мост (bridge) к `Hermes.Wpf` через файловый IPC и CLI-инструмент.

Отчёт не дублирует код — он навигационный: каждый раздел указывает, где в репозитории лежат соответствующие файлы, какие у них публичные контракты и как они связаны.

---

## Структура отчёта

| Файл | Содержание |
|---|---|
| [01_Architecture_Overview.md](./01_Architecture_Overview.md) | Состав решения, проекты, граф зависимостей, composition root, точки входа |
| [02_Domain_Model.md](./02_Domain_Model.md) | `TradingPlatformState`, `Position`, `Order`, `RiskProfile`, перечисления |
| [03_Events_And_Projections.md](./03_Events_And_Projections.md) | `IEventBus` / `InMemoryEventBus`, события платформы, проекции (MarketTick, EventLog, TradeJournal) |
| [04_Virtual_Exchange_And_Risk.md](./04_Virtual_Exchange_And_Risk.md) | `VirtualExchangeEngine`, обработка фиков, Auto SL/TP, `RiskValidator`, `RiskCircuitBreaker` |
| [05_Strategies_And_Orchestration.md](./05_Strategies_And_Orchestration.md) | `StrategyRunner`, встроенные стратегии (Momentum / MeanReversion / LiquiditySweep), `HermesOrchestrationService` |
| [06_Market_Data.md](./06_Market_Data.md) | Контракт `IMarketDataFeed`, `BinanceFuturesMarketDataFeed`, `MockMarketDataFeed`, переключение источника |
| [07_Persistence_And_Journal.md](./07_Persistence_And_Journal.md) | Все файловые хранилища (`%LocalAppData%/HermesTrading`), JSON vs SQLite журналы, восстановление сессии |
| [08_Bridge_And_CLI.md](./08_Bridge_And_CLI.md) | `TradingBridgePublisher`, `TradingBridgeCommandProcessor`, snapshot.json / commands.json / heartbeat.txt, `Hermes.TradingPlatform.Cli` |
| [09_WPF_Shell_And_ViewModels.md](./09_WPF_Shell_And_ViewModels.md) | `App.xaml.cs`, `ServiceContainerFactory`, навигация, страницы (Dashboard / Positions / Orders / Strategies / RiskManager / MarketWatch / Replay / Journal / Logs / Hermes / Account / Assistant / Settings) |
| [10_Findings_And_Recommendations.md](./10_Findings_And_Recommendations.md) | Сильные стороны, слабые места, риски, рекомендации |

---

## TL;DR — что собой представляет платформа

`Hermes.TradingPlatform` — это **WPF-приложение для бумажной (paper) фьючерсной торговли** на USDT-маржинальных контрактах Binance Futures с собственной виртуальной биржей, риск-движком, тремя встроенными стратегиями и оркестрационным наблюдателем Hermes (без LLM, чисто rule-based).

**Ключевые свойства:**

- **Только paper-режим**: все исполнения происходят в `VirtualExchangeEngine` с симуляцией taker fee (0.04%) и market slippage (0.02%). Реального ордеринга нет.
- **Источник котировок**: Binance USDT-M Futures public WebSocket (`wss://fstream.binance.com/ws/<symbol>@ticker`) + REST `/fapi/v1/ticker/24hr` для 24h-метрик. Альтернатива — `MockMarketDataFeed` (random walk).
- **Архитектура — event-driven**: `InMemoryEventBus` рассылает доменные события (`MarketTickEvent`, `OrderPlacedEvent`, `OrderFilledEvent`, `OrderCancelledEvent`, `PositionClosedEvent`, `RiskTriggeredEvent`, `StrategySignalEvent`, `PlatformLogEvent`). Проекции обновляют `TradingStateStore`, который является single source of truth UI.
- **Риск-контроль выполняется до исполнения**: `RiskValidator` проверяет лимиты (daily loss, per-trade margin, exposure, leverage, position size, safe mode reduce-only) синхронно при `PlaceOrder`. `RiskCircuitBreaker` независимо реагирует на изменения состояния и переключает `EmergencyHalt` при выходе за параметры.
- **Auto SL/TP**: при открывающем фике с включённым `AutoApplyDefaultSlTp` биржа автоматически выставляет reduce-only Stop (SL на расстоянии `MaxRiskPerTradePercent`) и Limit (TP = SL × `DefaultTakeProfitRrMultiplier`).
- **Hermes-оркестратор**: рулесет, который **только наблюдает и комментирует** (`Reviewing`, `Monitoring`, `Halted`). Не размещает ордера, не вмешивается в исполнение.
- **Персистентность**: `%LocalAppData%/HermesTrading/` — `session-state.json` (полный снимок), `trade_journal.jsonl` или `trade_journal.db` (SQLite), `risk-profile.json`, `platform-settings.json`, `strategy-parameters.json`.
- **Bridge → Hermes.Wpf**: отдельная папка `bridge/` с `snapshot.json` (DTO `TradingPlatformSnapshotFile`), `commands.json` (входящая очередь) и `heartbeat.txt` (живость <10 с). Сериализация выполнена через `System.Text.Json`.
- **CLI** (`Hermes.TradingPlatform.Cli`) — `status`, `is-running`, `enqueue <json>`, `wait-result <guid>` — для внешних автоматизаций и Hermes.Wpf-агента.

---

## Краткая карта потока данных

```
                ┌───────────────────────────────┐
                │ BinanceFuturesMarketDataFeed  │   ← WS @ticker + REST /24hr
                │ (или MockMarketDataFeed)      │
                └──────────────┬────────────────┘
                               │  MarketTickEvent
                               ▼
                       ┌───────────────┐
                       │  InMemoryEvent│
                       │     Bus       │
                       └───┬──────┬────┘
                           │      │
        MarketTick         │      │   OrderFilled/Placed/Cancelled,
                           │      │   StrategySignal, Risk*, Position*, Log
                           ▼      ▼
   ┌──────────────────────────┐   ┌────────────────────────────┐
   │ MarketTickProjection     │   │ TradeJournalProjection     │
   │ → state.Tickers/Positions│   │ → state.Journal +          │
   │   uPnL, equity           │   │   trade_journal.jsonl/db   │
   └──────────────────────────┘   └────────────────────────────┘
   ┌──────────────────────────┐   ┌────────────────────────────┐
   │ VirtualExchangeEngine    │   │ EventLogProjection         │
   │ (subscribes MarketTick   │   │ → state.Logs (top 200)     │
   │  to fill open orders)    │   └────────────────────────────┘
   └──────────────────────────┘
   ┌──────────────────────────┐   ┌────────────────────────────┐
   │ StrategyRunner           │   │ HermesOrchestrationService │
   │ → StrategySignalEvent +  │   │ → state.Hermes (reasoning, │
   │   (optional) PlaceOrder  │   │   decisions, tasks)        │
   └──────────────────────────┘   └────────────────────────────┘
   ┌──────────────────────────┐
   │ RiskCircuitBreaker       │
   │ → EmergencyHalt if daily │
   │   loss/exposure > cap    │
   └──────────────────────────┘
                               │
                               ▼
                ┌──────────────────────────────────┐
                │ TradingStateStore (single state) │
                └──────┬─────────────────────┬─────┘
                       │                     │
                       ▼                     ▼
            ┌──────────────────┐   ┌────────────────────────┐
            │ TradingReadModel │   │ TradingStatePersistence│
            │ (DTO для UI)     │   │ → session-state.json   │
            └────┬─────────┬───┘   └────────────────────────┘
                 │         │
                 │         └──→ TradingBridgePublisher → bridge/snapshot.json
                 ▼
            WPF страницы / страницы навигации
```

---

## Соглашения

- **Цены** — `decimal`, UTC-timestamps — `DateTimeOffset`.
- **OrderId** — `o-<seq>`, sequence начинается с 1003 и хранится в `session-state.json` (`NextOrderSequence`).
- **Reduce-only**: `Stop` = SL, `Limit` = TP (см. `TradingUiMapper.ToDto`).
- **JournalKind**: `Open` | `Add` | `Reduce` | `Close` (см. `PositionFillImpact`).
- **Снапшот UI** клонируется (`TradingStateStore.Clone`) — мутации идут через `Mutate(Action<...>)`.
- **Стандартный путь данных**: `%LocalAppData%/HermesTrading/` (Windows: `C:\Users\<user>\AppData\Local\HermesTrading`).

---

## Как читать отчёт

1. Если нужна **обзорная картина** — начните с `01_Architecture_Overview.md` и схемы выше.
2. Если нужно понять, **что попадает в Hermes.Wpf** — `08_Bridge_And_CLI.md` + AUDIT-комментарии в `RiskProfile.cs`, `VirtualExchangeEngine.cs`, `TradeJournalFileWriter.cs`.
3. Если нужно отследить **жизненный цикл сделки** — `04_Virtual_Exchange_And_Risk.md` + `03_Events_And_Projections.md` + `07_Persistence_And_Journal.md`.
4. Если интересует **рулесет Hermes-оркестратора** — `05_Strategies_And_Orchestration.md`, секция `HermesOrchestrationService`.
5. Если ищете **что улучшить** — `10_Findings_And_Recommendations.md`.
