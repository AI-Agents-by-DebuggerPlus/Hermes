# 05. Локальные обработчики (без LLM)

Hermes.Wpf может исполнить торговое действие **до** вызова Hermes CLI, если фраза однозначна. Это быстрее и не тратит токены.

## Порядок в ExecuteHermesUserTurnAsync

При `TradingModeEnabled`:

1. **Ранний проход** (до vision/desktop):
   - `TryHandleManualOrderLocalAsync`
   - `TryHandleClosePositionLocalAsync`

2. **Поздний проход** (после gate, status):
   - повтор manual / close (если не обработано ранее)

## Открытие позиции

### Классы

| Класс | Файл |
|-------|------|
| `TradingManualOrderHandler` | `Hermes.Wpf/Services/TradingManualOrderHandler.cs` |
| `TradingManualOrderParser` | `Hermes.Wpf/Services/TradingManualOrderParser.cs` |

### Алгоритм

1. `TryParseOpenRequest(text, knownSymbols, out draft, out price)`
2. Если цена не указана → **pending state**, вопрос «по рынку или лимит?»
3. `BuildCommand(draft, price, defaultUsdt)` → `TradingPlatformCommand`
4. `FuturesTradingCommandExecutor.ExecuteAsync` (или spot)

### Объём по умолчанию

`RiskBasedQuantityCalculator.ResolveDefaultUsdt(futuresSnapshot)`:

```
min(DefaultAgentOrderUsdt, MaxOrderNotionalUsdt, AvailableUsdt, headroom до MaxTotalExposureUsdt)
```

### Примеры распознаваемых фраз

- «открой лонг по биткоину по рынку»
- «открой шорт ETH на 100 USDT»
- «купи биткоин лимит 90000»

### Отмена pending

«нет» / cancel → «Открытие позиции отменено».

## Закрытие позиции

| Класс | Назначение |
|-------|------------|
| `TradingClosePositionTriggers` | Regex «закрой позицию», «закрой шорт на BTC» |
| `TradingSymbolResolver` | Извлечение символа из текста |
| `TryHandleClosePositionLocalAsync` | `close_position` через executor |

Close **не** проходит через open-parser (guard в `TradingManualOrderParser`).

## Статусные запросы

`TryHandleTradingStatusQueryLocalAsync` — только при trading mode.

| Intent | Formatter |
|--------|-----------|
| BalanceOnly | `FuturesTerminalStatusReplyFormatter.FormatBalanceOnly` |
| AccountSummary | `FormatAccountSummary` |

Классификатор: `TradingQueryIntentClassifier` (те же маркеры, что и для промпта).

**Без LLM** — ответ из snapshot на диске.

## Когда локальный путь не срабатывает

- Символ не распознан → подсказка с примером (если `LooksLikeTradeIntent`)
- Терминал не запущен → `EnsureTerminalReadyAsync` в executor
- Сложная стратегия / несколько условий → fallback на агента

## Сравнение путей

| Критерий | Локальный | Агент |
|----------|-----------|-------|
| Скорость | Высокая | Зависит от LLM |
| Гибкость | Шаблонные фразы | Произвольный язык |
| Правила безопасности | Только RiskManager терминала | + текстовые правила в промпте |
| Объём USDT | `RiskBasedQuantityCalculator` | Агент + тот же snapshot |
