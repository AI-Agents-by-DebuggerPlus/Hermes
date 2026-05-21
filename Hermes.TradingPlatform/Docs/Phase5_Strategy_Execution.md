# Phase 5 — Strategy execution (реализовано, MVP)

## Архитектура

```
MarketTickEvent → StrategyRunner → ITradingStrategy.Evaluate()
                      ↓
              StrategySignalEvent → Logs
                      ↓ (auto-exec, if enabled & no halt)
              IVirtualExchange.PlaceOrder() → RiskValidator
```

## Core

- `ITradingStrategy`, `StrategySignal`
- `StrategySignalEvent`

## Strategies (`Hermes.TradingPlatform.Strategies`)

| ID | Класс | Логика (paper MVP) |
|----|--------|---------------------|
| `liq-sweep` | `LiquiditySweepStrategy` | SOL, \|24h\| ≥ 2% → limit sweep |
| `momentum` | `MomentumStrategy` | BTC, 24h > 0.6% → market long 0.01 |
| `mean-rev` | `MeanReversionStrategy` | ETH, \|24h\| > 0.8% → fade (market) |

Cooldown 45–90 с на стратегию, чтобы не спамить ордерами.

## UI

- **Strategies** → Enable/Disable → `TradingPlatformHost.SetStrategyEnabled` → state + runner
- **Logs** — строки `Strategy` при сигналах
- **Emergency Stop** — runner не исполняет (проверка `EmergencyHalt`)

## Ограничения MVP

- Параметры стратегий не в UI (см. T-O04 в [TASKS.md](TASKS.md))
- Нет backtest
- Safe Mode блокирует не-RO ордера от стратегий (как ручные)

## Дальше

**Phase 6** — Hermes orchestration. См. [TASKS.md](TASKS.md).
