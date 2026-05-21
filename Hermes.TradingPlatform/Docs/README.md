# Hermes Trading Platform

Модульная платформа для **paper trading**, симуляции криптобиржи, исполнения стратегий и управления рисками. Совместима с экосистемой Hermes; **не является торговым ботом**.

## Фазы разработки

| Фаза | Статус | Содержание |
|------|--------|------------|
| **1** | ✅ | UI-only, mock data, WPF terminal |
| **2** | ✅ | Domain models, in-memory event bus, state store |
| **3** | ✅ MVP | Virtual exchange, mock ticks, new order, risk persist |
| **4** | ✅ | Binance Futures public WebSocket (ticker) |
| **5** | ✅ MVP | Strategy runner, 3 built-in strategies, auto-exec |
| **6** | ✅ MVP | Hermes orchestration (monitor / explain / review) |

## Solution

`Hermes.TradingPlatform.sln`

| Проект | Назначение |
|--------|------------|
| `Hermes.TradingPlatform.Wpf` | Trading terminal UI (MVVM) |
| `Hermes.TradingPlatform.Shared` | DTO / shared types |
| `Hermes.TradingPlatform.Core` | Abstractions, events (Phase 2+) |
| `Hermes.TradingPlatform.Exchange` | Virtual exchange (Phase 3+) |
| `Hermes.TradingPlatform.Risk` | Risk engine (Phase 3+) |
| `Hermes.TradingPlatform.Strategies` | Strategies (Phase 5+) |
| `Hermes.TradingPlatform.Orchestration` | Hermes layer (Phase 6) |
| `Hermes.TradingPlatform.Data` | SQLite / PostgreSQL (Phase 2+) |

## Запуск

```powershell
dotnet build Hermes.TradingPlatform.sln -c Release
.\Hermes.TradingPlatform.Wpf\bin\Release\net8.0-windows\Hermes.TradingPlatform.exe
```

## Архитектурные правила

- UI не содержит бизнес-логики биржи
- Event-driven, modular, replayable
- Hermes не исполняет ордера и не обходит risk manager

- [Phase 1 — UI](Phase1_UI_Spec.md)
- [Phase 2 — State & events](Phase2_State_And_Events.md)
- [Phase 3 — Virtual exchange](Phase3_Virtual_Exchange.md)
- [Phase 4 — Binance market data](Phase4_Binance_Market_Data.md)
- [Phase 5 — Strategy execution](Phase5_Strategy_Execution.md)
- [Phase 6 — Hermes orchestration](Phase6_Hermes_Orchestration.md)
- [Список задач (TASKS)](TASKS.md)
