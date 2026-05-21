# Hermes Trading Platform

Модульная платформа для **paper trading**, симуляции криптобиржи, исполнения стратегий и управления рисками. Совместима с экосистемой Hermes; **не является торговым ботом**.

## Фазы разработки

| Фаза | Статус | Содержание |
|------|--------|------------|
| **1** | ✅ Текущая | UI-only, mock data, WPF terminal |
| 2 | План | Domain models, event bus |
| 3 | План | Virtual exchange core |
| 4 | План | Binance WebSocket market data |
| 5 | План | Strategy execution |
| 6 | План | Hermes orchestration (без bypass risk) |

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

См. также спецификацию в корневом TASK (чат / issue).
