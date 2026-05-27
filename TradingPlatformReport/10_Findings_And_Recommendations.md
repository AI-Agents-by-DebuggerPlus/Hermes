# 10. Выводы и рекомендации

Этот раздел подытоживает аудит. Он структурирован как: (а) сильные стороны, (б) слабые места по доменам, (в) приоритетные рекомендации, (г) рискт-карта.

---

## 10.1. Сильные стороны архитектуры

### Чётко выделенные слои

- **`Core` / `Data` / `Exchange` / `Shared` / `Wpf` / `Cli`** — продуманное разделение. Core не зависит от UI; Wpf зависит от всех. Это поддерживает testability и сменность UI (можно теоретически добавить Avalonia/Maui-фронт без рефакторинга domain'а).
- Доменные модели (`TradingPlatformState`, `Position`, `Order`, `RiskProfile`, `TradingAccount`, `PnlTracker`) — POCO без зависимостей от WPF/DI/файлов. Это идеал для domain-слоя.

### Event-driven с единым state store

- `InMemoryEventBus` + `TradingStateStore.Mutate` — простая и работоспособная замена Redux/CQRS. Все изменения проходят через события, что облегчает аудит и replay.
- `MarketTickProjection` + `EventLogProjection` + `TradeJournalProjection` — каждый аспект state отражён отдельной проекцией. Расширение нового события — это новый Subscribe + новый проектор, без затрагивания существующих.
- `StateChanged` event позволяет UI и bridge единообразно реагировать на любое изменение.

### Paper-trading реалистичен

- `VirtualExchangeEngine` учитывает taker fee (0.04%) и market slippage (0.02%). Это лучше, чем infinity liquidity / zero cost mock'и, которые часто встречаются в учебных платформах.
- `PositionFillImpact` корректно отрабатывает open/add/reduce/close + side-flip — это полная семантика futures-account'а в hedge-mode.
- Auto SL/TP — practical feature для бесстрессовой ручной торговли.

### Полноценный risk-механизм

- **Two-tier**: `RiskValidator` (pre-trade reject) + `RiskCircuitBreaker` (post-trade auto-shutdown). Это правильный паттерн.
- Все события risk-нарушения логируются через `RiskTriggeredEvent`, видны в Logs и в bridge snapshot.
- `EmergencyHalt` persistent (через `RiskProfileFileStore`) — survives restart.

### Файловая persistence продумана

- Атомарная запись через tmp+File.Replace в основных файлах.
- Разделение `session-state.json` ↔ `risk-profile.json` ↔ `platform-settings.json` ↔ `strategy-parameters.json` — каждый файл за своё. При повреждении одного, остальные не теряются.
- Восстановление сессии (positions, orders, journal) после крэша — работает out-of-the-box.
- Поддержка двух журналов (JSON Lines / SQLite) с одинаковым контрактом `IJournalStore`.

### Bridge IPC — pragmatic

- Файловый IPC без сокетов и DI-зависимостей между процессами. Хорошо работает на одной машине, легко дебажить (можно открыть JSON руками).
- Heartbeat-механизм с TTL 10–12 с.
- Command-result async-протокол с `wait-result` для CLI.

### Live market data с graceful degradation

- `BinanceFuturesMarketDataFeed` с auto-reconnect через `WebSocketResilience`.
- Любая ошибка → переключение на `MockMarketDataFeed` или `Disconnected` статус, без падения приложения.
- 15-секундный stagnation-detector — отлавливает «тихий» drop соединения.

### Hermes-оркестратор разумно ограничен

- Чистый rule-based observer, **не торгует**. Это снижает риск катастрофических багов.
- Понятный output: `currentReasoning` + `decisions` + `tasks` — этого хватает для UI и для bridge.

### Унифицированный UI-слой

- `TradingReadModel` — single point of read. Любой UI-компонент работает только через DTO.
- `TradingPageViewModel` — единый базовый класс с автообновлением.
- `ViewLocator` — типобезопасный manual mapping VM ↔ View.

---

## 10.2. Слабые места по доменам

### 10.2.1. Domain / State

| Проблема | Где | Описание |
|---|---|---|
| `FreeMargin`/`UsedMargin` **не пересчитываются** | `TradingAccount` | При открытии позиции `Account.UsedMargin` остаётся seed-значением. Это означает, что risk-метрика "exposure" работает на грубой `Margin = Size×MarkPrice/Leverage`, а в UI отображается нерелевантное значение. |
| `LiquidationPrice` **статичен** | `Position` | Считается только в seed; не обновляется при движении цены или fee. Опасно показывать его пользователю как "live ликвидация". |
| `Pnl.WinRate/SharpeRatio/MaxDrawdown` **не считаются** | `PnlTracker` | Хранятся в state, но никто их не апдейтит. UI показывает 0. |
| `Position.Side` для нулевой позиции | `Position` | После полного `Close` позиция остаётся в state с `Size=0` и старым `Side`. Это может ввести в заблуждение filter-логику. |
| Decimal precision в SQLite-журнале | `SqliteJournalStore` | Поля `Quantity`, `FillPrice`, `Fee`, `RealizedPnl`, `BalanceBefore/After` хранятся как `REAL` (`double`). При больших AllTime суммах копится floating-point дрейф. |

### 10.2.2. Exchange / Risk

| Проблема | Где | Описание |
|---|---|---|
| **Limit-ордера** не имитируют slippage | `VirtualExchangeEngine.FillLimitOrder` | Заполняются точно по limit-цене. В реальности limit может частично исполниться или вообще нет. Это делает paper-trading "идеализированным" для limit-стратегий. |
| **Risk-валидация только при PlaceOrder** | `RiskValidator` | После открытия, если рынок ушёл и exposure вырос — ничего не падает (только circuit-breaker). |
| `CalculateRequiredMargin` не учитывает кросс-направление | `RiskValidator` | Если у вас уже Long BTC и вы открываете дополнительный Long — required margin наращивается. Если открываете Short (hedge) — он тоже наращивается, хотя в реальности hedge снижает netto margin. |
| `RiskCircuitBreaker` НЕ снимает `EmergencyHalt` обратно | `RiskCircuitBreaker` | Оператор должен вручную снять через `RiskManagerViewModel.EmergencyHalt`. Это правильно для безопасности, но непонятно из UI hint'а. |
| Constants 0.04% fee / 0.02% slippage **захардкожены** | `VirtualExchangeEngine` | Не настраиваются. Для других бирж (Bybit, OKX) числа другие. |

### 10.2.3. Стратегии

| Проблема | Где | Описание |
|---|---|---|
| Стратегии не имеют **back-test mode** | `StrategyRunner` | Невозможно прогнать стратегию на historical data — только live. |
| Нет **stop-loss/take-profit** на уровне стратегии | `ITradingStrategy` | Стратегия только генерирует сигнал. SL/TP — задача `Auto SL/TP` в Exchange (которая берёт **глобальные** настройки). Это значит, что разные стратегии не могут иметь разные SL-multiplier'ы. |
| `LiquiditySweepStrategy` использует только две точки | `LiquiditySweepStrategy` | Sweep detection упрощён до сравнения текущей цены с recent max/min — это даёт много false positives. |
| Cooldown — глобальный для всех символов стратегии | `StrategyCooldown` | Если стратегия торгует BTCUSDT и ETHUSDT — cooldown общий, не per-symbol. |

### 10.2.4. Hermes Orchestration

| Проблема | Где | Описание |
|---|---|---|
| Rule-based и нечитаемо | `HermesOrchestrationService` | "Reasoning" — это конкатенация условий, не natural language. Для пользователя малоинформативно. |
| `Tasks` и `Decisions` — append-only без TTL | `HermesState` | Со временем растут; в UI без пагинации это становится дорогим. |
| `confidence` — статичен (0/0.5/1) | — | Не отражает реального состояния (например, силы тренда). |

### 10.2.5. Market Data

| Проблема | Где | Описание |
|---|---|---|
| Только 3 символа в seed | `InitialTradingSeed` | BTCUSDT, ETHUSDT, SOLUSDT. Нет UI для добавления — нужно править код. |
| Один WebSocket connection per symbol | `BinanceFuturesMarketDataFeed.SubscribeToSymbolsAsync` | Для каждого символа отдельный поток + ConnectAsync. При 50 символах это будет 50 socket'ов. Лучше использовать combined stream `/stream?streams=btcusdt@ticker/ethusdt@ticker/...`. |
| `BookTicker` поток не используется | `IMarketDataFeed.SubscribeBookTickerAsync` | Контракт есть, но UI и матчинг используют только Ticker (last trade price). Это значит, что Bid/Ask не пробрасываются, и market-order slippage упрощён. |
| Нет orderbook | — | На futures-режиме нет видимости стакана — это норма для paper, но для серьёзного бэктеста было бы полезно. |

### 10.2.6. Persistence

| Проблема | Где | Описание |
|---|---|---|
| Нет миграций схемы | `TradingSessionStateFileStore` | `Version=1` есть, но логика migrate отсутствует. При смене схемы — поломка. |
| `MergeMissingSeedTickersAndStrategies` неотменяемо | — | Если пользователь удалил тикер (теоретически), он снова появится после restart. В UI удалить нельзя — спорная UX. |
| `trade_journal.jsonl` без пруна | `TradeJournalFileWriter` | Со временем файл становится огромным; `LoadAll` (для Replay) грузит всё. |
| `bridge/result-*.json` накапливаются | `TradingBridgeCommandProcessor.WriteResult` | Файлы не удаляются. После недели работы — несколько тысяч файлов. |
| `platform-settings.json` без атомарной записи | `PlatformSettingsFileStore` | Прямой `WriteAllText`. Power loss → потеря настроек (приемлемо для defaults, но всё же). |

### 10.2.7. Bridge / Hermes.Wpf integration

| Проблема | Где | Описание |
|---|---|---|
| `RecentLogs.Take(15)` — узкий канал | `TradingBridgePublisher.BuildSnapshot` | При burst'е событий (например, серия fills) экспортёр пропустит часть. |
| Trade journal **не виден** через bridge | — | Hermes.Wpf не может ингестить полную историю сделок без прямого чтения `trade_journal.*`. Это нарушение слоёв. |
| `OrderSnapshot` не несёт SL/TP | `TradingPlatformSnapshotFile` | reduce-only флаг есть, но не понятно, это SL или TP. Hermes.Wpf делает heuristics (price relative to entry). |
| RiskProfile конфиг **не виден** через bridge | — | Hermes.Wpf не знает `MaxRiskPerTradePercent`, `MaxLeverage` и т.д. Он может только наблюдать `RiskLevel` и `DD%`. |
| `Journal` пустой через bridge | — | Wpf-агент не может проанализировать историю сделок, чтобы дать совет. |
| Не атомарная запись `commands.json` (через CLI) | `Program.cs.CmdEnqueue` | Прямой `WriteAllText` без tmp+Replace. Race-condition с reader. |
| Polling каждую секунду | `TradingBridgeCommandProcessor` | Latency до 1 с. Для chat-команд приемлемо, для HFT нет. |

### 10.2.8. WPF / UX

| Проблема | Где | Описание |
|---|---|---|
| Все 12 страничных VM **live одновременно** | `MainViewModel._pages` | Каждое `StateChanged` пробуждает все 12 `Refresh()`. Для маленького dataset'а ОК, но проигрывает page-on-demand pattern'у. |
| `ObservableCollection.Clear()/Add()` в `Refresh` | Большинство VM | Сбрасывает selection / scroll / sort. Лучше дифф (как в `StrategiesVM`). |
| Нет валидации **разумности** в Risk Manager | `RiskManagerViewModel.TryBuildSettings` | TryParse — единственная проверка. Можно поставить `MaxLeverage=1000` или отрицательные числа. |
| `LiveContextSnapshot` для assistant **минималистичный** | `TradingInAppAssistantContextProvider` | Не передаёт positions, orders, risk-лимиты. Assistant в Trading Platform не может отвечать на торговые вопросы по существу. |
| `Replay` не показывает позиции по ходу | `JournalReplayService` | Только сами entries, но не реконструкция portfolio state на каждом шаге. |

---

## 10.3. Приоритетные рекомендации

### P1 — Безопасность и корректность данных

1. **Пересчитывать `Account.UsedMargin` и `FreeMargin` при каждом fill / mark-price-update**. Это критично для корректной exposure-метрики.
2. **Пересчитывать `Position.LiquidationPrice` динамически** или вообще не показывать его в UI до тех пор, пока он не верен. Текущее значение вводит в заблуждение.
3. **Атомарная запись** для `platform-settings.json`, `risk-profile.json` и `trade_journal.jsonl` (через tmp + File.Replace).
4. **Валидация диапазонов** в Risk Manager (MaxLeverage 1..125, MaxRiskPerTrade 0.1..10, и т.п.).
5. **Pruner** для `bridge/result-*.json` (например, удалять > 1 час) и для старого session-log'а сделать аналогичный для `trade_journal.jsonl` (rolling по размеру).

### P2 — Bridge расширения для Hermes.Wpf

6. Добавить в `TradingPlatformSnapshotFile` поля **RiskProfile config**: `MaxRiskPerTradePercent`, `MaxLeverage`, `MaxExposurePercent`, `AutoApplyDefaultSlTp`, `DefaultTakeProfitRrMultiplier`. Это даст Hermes.Wpf понимание лимитов.
7. Добавить **`RecentJournal[]`** (последние 30–50 entries) в snapshot. Это позволит assistant'у видеть свежую историю сделок для анализа.
8. Увеличить `RecentLogs` лимит с 15 до 50 или ввести "important-only" фильтр (Risk, OrderFilled).
9. Включить `Position.StopLoss`/`TakeProfit` в `PositionSnapshot` (рассчитанные из связанных reduce-only orders).
10. Атомарная запись `commands.json` в CLI (через tmp + Replace).

### P3 — Domain полнота

11. **Реализовать `PnlTracker.WinRate / SharpeRatio / MaxDrawdown`**. Они уже в state — нужны проекции/calculators.
12. **`PositionFillImpact`** — при полном закрытии позиции (`Size=0`) удалять её из `state.Positions` (или сбрасывать `Side`), чтобы фильтры работали корректно.
13. **Per-symbol cooldown** в `StrategyCooldown`.
14. **Per-strategy SL/TP параметры** — расширить `StrategyParameters`.

### P4 — Performance и UX

15. **Lazy-init страничных VM** через factory pattern: `_pages[NavigationPage.X] = () => sp.Resolve(...)`. Это снизит overhead при `StateChanged`.
16. Использовать **дифф-обновление** в `OrdersViewModel`, `PositionsViewModel`, `JournalViewModel`, `LogsViewModel` (по аналогии со `StrategiesViewModel`).
17. **UI для добавления символа**: ввод тикера → проверка через REST `/fapi/v1/exchangeInfo` → подписка на WS.
18. **Combined WebSocket stream** для Binance: один сокет на N символов.
19. **Расширить `TradingInAppAssistantContextProvider`**: добавить `Positions[]`, `Orders[]`, `RiskLimits`, `LastDecisions[]`.

### P5 — Backtesting и Replay

20. **Backtest mode** для стратегий: прокрутить journal через `StrategyRunner` оффлайн.
21. **State replay**: показывать portfolio reconstruction по journal entries (`Position` и `PnL` на каждом шаге).

### P6 — Технический долг

22. `VirtualExchangeEngine` константы (fee, slippage) — вынести в `ExchangeSettings` (для config'а).
23. Добавить **schema-миграции** для `TradingSessionStateFile` (Version 2, 3, ...).
24. SQLite журнал — заменить `REAL` на `TEXT` (decimal-as-string) или `INTEGER` (cents) для денежных полей.
25. Тесты: сейчас в `Hermes.TradingPlatform.Tests` есть unit-тесты для exchange и risk; нужно расширить на projection-pipeline, persistence round-trip и bridge command flow.

---

## 10.4. Карта рисков

| Риск | Вероятность | Влияние | Митigation |
|---|---|---|---|
| Поломка `session-state.json` при power-loss | Низкая | Высокое (потеря баланса/позиций) | Атомарная запись (есть). Бэкап перед каждой записью? |
| Binance WS rate-limit / IP-ban | Низкая (paper, не торгуем) | Среднее (потеря котировок) | Auto-reconnect есть. При >5 reconnect/min — переключиться на mock? |
| Расхождение `Account.UsedMargin` ↔ реальной exposure | **Высокая** | **Высокое** (ложный sense of safety) | См. P1.1. |
| `EmergencyHalt` залипнет при перезапуске и блокирует торговлю | Средняя | Среднее (UX, не безопасность) | UI hint о том, как снять. |
| Crash во время записи `commands.json` через CLI | Низкая | Низкое (запрос потеряется) | Атомарная запись. |
| Огромный `trade_journal.jsonl` (>1 GB) → Replay OOM | Средняя | Среднее | Rolling-pruner + streaming read. |
| Hermes.Wpf assistant даёт неверный совет из-за `RecentLogs.Take(15)` | Средняя | Среднее | P2 (расширение bridge). |
| Изменение Binance API формата | Низкая | Высокое (потеря feed'а) | Defensive parsing (есть, без exceptions). |

---

## 10.5. Что НЕ требует немедленных изменений

- **`HermesOrchestrationService` как rule-based observer** — это правильное решение. Не нужно переписывать в LLM-based, пока это просто наблюдатель.
- **Файловый IPC bridge** — для двух WPF-приложений на одной машине это адекватно. Сокеты/gRPC = overkill.
- **`Hermes.InAppAssistant` через OpenRouter** — отказ от прямой Gemini/OpenAI интеграции хорошо изолирует от vendor lock-in.
- **Manual `ViewLocator`** — типобезопасный и compile-time проверяемый. Лучше DataTemplate'ов для этого размера приложения.
- **Two separate apps (Hermes.Wpf и Hermes.TradingPlatform)** — правильное разделение интерфейсов. Один — paper-terminal, другой — orchestrator/coach.

---

## 10.6. Заключение

`Hermes.TradingPlatform` — это **зрелый и продуманный paper-trading терминал** с правильной слоистой архитектурой, event-driven core, реалистичной симуляцией исполнения и надёжной персистентностью. Основные слабости — это (а) недореализованные доменные метрики (`UsedMargin`, `LiquidationPrice`, `WinRate`), (б) узкие места в bridge для интеграции с Hermes.Wpf (`RecentLogs[15]`, отсутствие journal в snapshot), (в) hardcoded константы exchange (fee/slippage) и (г) минималистичный assistant-контекст.

Все обнаруженные слабости — **technical debt уровня P2–P3** (приоритет средний), не критические баги. Платформа в текущем виде уже пригодна для практической работы как песочница для отработки торговых рефлексов и для обучения Hermes-агента (через external brain).

Рекомендую двигаться по P1 (корректность данных) → P2 (bridge для AI-агента) → P3 (доменная полнота). P4–P6 — по мере роста usage.
