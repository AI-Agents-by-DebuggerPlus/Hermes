# 09. Диагностика и типичные проблемы

## Логи Hermes.Wpf

Префиксы в `LogService` / system messages чата:

| Префикс | Смысл |
|---------|--------|
| `[trading-mode]` | Вход/выход режима, persona |
| `[futures-bridge]` | Enqueue, wait result, read snapshot |
| `[futures-cmd]` / `[spot-cmd]` | Отправленная команда |
| `[futures-result]` | Результат bridge |
| `[trade-open]` | Локальный manual order handler |
| `[trading-status]` | Локальный status query |
| `[trading-bridge]` | Unified / legacy trading platform |

## Логи терминала

Журнал UI + `[REST]` в `BinanceApiService`:

- `POST /fapi/v1/order`
- `POST /fapi/v1/marginType`
- `[trade-stats]` — расчёт PnL за периоды

## Проверка живости bridge

```powershell
# Heartbeat (должен обновляться каждые ~5 с)
Get-Content "$env:LOCALAPPDATA\HermesFutures\bridge\heartbeat.txt"

# Snapshot
Get-Content "$env:LOCALAPPDATA\HermesTrading\bridge\snapshot.json" | Select-Object -First 40
```

## Чеклист «ордер не прошёл»

| # | Проверка |
|---|----------|
| 1 | `TradingModeEnabled == true` |
| 2 | Терминал запущен, heartbeat < 12 с |
| 3 | `HasCredentials: ok` в snapshot |
| 4 | Агент вернул JSON с `"skill":"trading"` |
| 5 | `RiskManager` не отклонил (MessageBox в терминале / result message) |
| 6 | Для Unicode-символов — POST body (не URL query) для marginType |

## Типичные симптомы

### «Snapshot пуст — дождитесь bridge»

- Терминал не запущен или нет API keys  
- Первые секунды после старта — дождаться publish  

### Агент отвечает текстом, ордер не уходит

- Нет JSON в ответе (status query — норма)  
- Агент применил **правила безопасности** и отказал (ожидаемо)  
- `NormalModeGuardRu` — trading mode выключен  

### «Signature for this request is not valid» (margin mode)

- Исправлено: POST с телом form-urlencoded для signed requests с Unicode symbol  
- Массовое применение margin mode: ошибки отдельных символов → журнал, не блокируют сохранение checkbox  

### Локальный парсер перехватил, агент не вызывался

- Фраза совпала с `TradingManualOrderParser`  
- Правила безопасности **не** применяются на этом пути (только RiskManager)  

### Объём не тот

- Проверить `DefaultAgentOrderUsdt`, `MaxOrderMarginPercent` в терминале  
- Snapshot: `MaxOrderNotionalUsdt`, `AvailableUsdt`  
- `RiskBasedQuantityCalculator` для local path  

## Таймауты

| Операция | Timeout |
|----------|---------|
| WaitResult place_order | 20 с |
| WaitResult close | 75 с |
| Close PnL poll | до 60 с (`CloseRealizedPnlPoller`) |
| ChatTimeoutSeconds | 180 с (Settings) |

## Полезные классы для отладки

| Задача | Класс |
|--------|-------|
| Разбор JSON агента | `TradingPlatformIntentParser` |
| Формат сообщений чата | `TradingExecutionMessages` |
| USDT display | `ChatUsdtFormatter` |
| Gate trading | `TryHandleTradingModeGateLocal` |
| Snapshot text | `BuildFuturesContextBlockRu` |

## Связанные отчёты

- [Trading Platform Implementation Audit](../../../TradingPlatformReport/README.md) — отдельная paper-платформа  
- [Binance Demo Futures Backlog](../../Plans/BinanceDemoFutures_Review_Backlog.ru.md) — backlog улучшений  
