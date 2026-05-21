# Phase 2 — Domain & event bus (реализовано)

## Core (`Hermes.TradingPlatform.Core`)

### Domain models
- `TradingAccount`, `Position`, `Order`, `RiskProfile`, `StrategyState`, `HermesState`
- `MarketTicker`, `PnlTracker`, `PlatformLogEntry`, `TradingPlatformState`

### Events
- `MarketTickEvent`, `OrderPlacedEvent`, `OrderFilledEvent`, `OrderCancelledEvent`
- `PositionClosedEvent`, `RiskTriggeredEvent`, `PlatformLogEvent`
- `IEventBus` + `InMemoryEventBus`

### Abstractions
- `ITradingStateStore`, `IVirtualExchange`, `IRiskValidator`
- `TradingUiMapper` — domain → UI DTO (`Shared.Mock`)

## Data (`Hermes.TradingPlatform.Data`)
- `TradingStateStore` — thread-safe mutable state + `StateChanged`
- `InitialTradingSeed` — стартовые данные (бывший mock)
- `MarketTickProjection` — тики → mark price, unrealized PnL, equity
- `EventLogProjection` — все события → лента Logs

## WPF bridge
- `TradingPlatformHost` — composition root
- `TradingReadModel` — API для ViewModels, подписка на `StateChanged`
- `TradingPageViewModel` — базовый refresh по событиям

UI layout **не менялся** — только источник данных.
