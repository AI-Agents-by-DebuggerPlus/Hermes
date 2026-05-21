# Hermes Trading Platform — список задач

Обновляется по мере разработки. См. также [README](README.md).

---

## Завершено

| ID | Задача | Фаза |
|----|--------|------|
| T-01 | UI terminal, 10 страниц, mock data | 1 |
| T-02 | Domain models + `InMemoryEventBus` + `TradingStateStore` | 2 |
| T-03 | Virtual exchange (market/limit/stop, fill on touch, fees) | 3 |
| T-04 | Mock market feed + live UI refresh | 3 |
| T-05 | Форма «Новый ордер» + Cancel | 3 |
| T-06 | Risk limits → state + `risk-profile.json` | 3 |
| T-07 | Binance Futures `@ticker` WebSocket + переключение Mock/Live | 4 |
| T-08 | Settings: market data source, статус feed в top bar | 4 |

---

## Завершено (продолжение)

| ID | Задача | Фаза |
|----|--------|------|
| T-09 | Strategy runner + изолированные стратегии | 5 |
| T-10 | Enable/disable стратегий → state + runner | 5 |
| T-11 | Сигналы в Logs (`StrategySignalEvent`) | 5 |
| T-12 | Авто-исполнение сигналов через virtual exchange + risk | 5 |

## Завершено (Phase 6)

| ID | Задача | Фаза |
|----|--------|------|
| T-20 | Hermes orchestration layer (`HermesOrchestrationService`) | 6 |
| T-21 | Без исполнения ордеров / bypass risk (только Core, no Exchange) | 6 |

## В работе / следующая

| ID | Задача | Фаза | Статус |
|----|--------|------|--------|
| T-22 | Replay backend | — | План |

---

## Запланировано (основной roadmap)

| ID | Задача | Фаза |
|----|--------|------|
| T-20 | Hermes orchestration layer (monitor / explain / review) | 6 |
| T-21 | Hermes не исполняет ордера и не обходит Risk Manager | 6 |
| T-22 | Replay backend (play / pause / speed / jump) | — |
| T-23 | SQLite / PostgreSQL persistence (ордера, сессии) | Data |
| T-24 | SignalR / realtime UI (опционально для multi-client) | — |
| T-25 | Modify order, partial fills, order book | Exchange |
| T-26 | Свечи Binance (`@kline`) | 4+ |
| T-27 | Исполнение на реальной бирже (API keys) — вне MVP paper | — |

---

## Опционально (отложено)

| ID | Задача | Заметки |
|----|--------|---------|
| **T-O01** | **Trades WebSocket** (`@aggTrade` / `@trade`) | Поток сделок для тиковой ленты, replay-записи, стратегий по объёму. Сейчас достаточно `@ticker`. |
| **T-O02** | **Единое плечо (leverage sync)** | Связать Settings «Default leverage» ↔ `Account.Leverage` ↔ отображение в Risk/Dashboard; отделить от `RiskProfile.MaxLeverage` (потолок). |
| T-O03 | Запись тиков на диск для Replay | Зависит от T-O01 или ticker log |
| T-O04 | Per-strategy параметры (JSON / UI) | После T-09 |
| T-O05 | Backtest mode на исторических данных | — |
| T-O06 | ASP.NET Core API + React terminal | Альтернатива WPF |

---

## Phase 5 — критерии готовности (T-09…T-12)

- [x] `ITradingStrategy` + реализации (Liquidity Sweep, Momentum, Mean Reversion)
- [x] `StrategyRunner` подписан на `MarketTickEvent`
- [x] Toggle на странице Strategies меняет `StrategyState` в store
- [x] Сигнал → лог; при включённом auto-exec → `PlaceOrder` с проверкой risk
- [x] Emergency halt останавливает auto-exec

## Phase 6 — критерии (T-20…T-21)

- [x] Hermes page получает данные из orchestration service (не mock-only)
- [x] Нет прямого вызова биржи из Hermes layer

---

*Последнее обновление: Phases 1–6 MVP готовы; далее — Replay, persistence, опциональные T-O01/T-O02.*
