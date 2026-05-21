# Phase 6 — Hermes orchestration (реализовано, MVP)

## Принципы

| Hermes делает | Hermes не делает |
|---------------|------------------|
| Monitor — обновляет reasoning по state | PlaceOrder / Cancel |
| Explain — strategy context, risk summary | Обход RiskValidator |
| Review — записи в Decisions | Прямой доступ к `IVirtualExchange` |

Проект **`Hermes.TradingPlatform.Orchestration`** ссылается **только на Core** (без Exchange).

## `HermesOrchestrationService`

Подписки на event bus:

- `MarketTickEvent` — refresh reasoning (~20 с)
- `StrategySignalEvent` — review + decision log
- `RiskTriggeredEvent` — halt + urgent task
- `OrderFilledEvent` / `OrderPlacedEvent` — post-trade review (observation)

Обновляет `HermesState` в `ITradingStateStore`:

- `CurrentReasoning`, `StrategyContext`, `Confidence`, `ActiveStrategy`
- `Tasks`, `Decisions` (кольцевой буфер)

## UI

- **Hermes** — live данные из state (не статический mock-текст)
- **Settings** → Enable Hermes orchestration → `platform-settings.json`
- **Logs** — источник `Hermes` / `Orchestrator`
- **Dashboard** widget — `HermesStatus` из того же state

## Дальше (вне MVP)

- Интеграция с Hermes WPF / External Brain / LLM (объяснения)
- Phase 6 не подключает Supabase и не вызывает AI

См. опциональные задачи в [TASKS.md](TASKS.md).
