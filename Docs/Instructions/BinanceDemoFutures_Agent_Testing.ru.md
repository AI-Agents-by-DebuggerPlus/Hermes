# Как протестировать Hermes ↔ Binance Demo Futures Terminal

Краткая памятка по проверке file-bridge: чтение данных с **demo-fapi.binance.com** и исполнение торговых команд через **Hermes.BinanceDemoFuturesTerminal.exe**.

## Архитектура

```
Hermes.Wpf (чат, режим трейдинга)
    ↓ читает snapshot.json
    ↓ пишет commands.json
Hermes.BinanceDemoFuturesTerminal.exe
    ↓ REST / WebSocket
demo-fapi.binance.com
```

| Путь | Назначение |
|------|------------|
| `%LocalAppData%\HermesTrading\bridge\snapshot.json` | Unified snapshot (секция `FuturesTerminal`) |
| `%LocalAppData%\HermesFutures\bridge\commands.json` | Очередь команд от Hermes.Wpf |
| `%LocalAppData%\HermesFutures\bridge\heartbeat.txt` | Heartbeat futures-терминала (&lt; 12 с = жив) |
| `%LocalAppData%\HermesFutures\bridge\result-{guid}.json` | Результат команды |

Ключевые файлы в репозитории:

- `Hermes.BinanceDemoFuturesTerminal/Bridge/` — publisher + command processor
- `Hermes.Wpf/Services/FuturesTerminalBridgeService.cs` — клиент bridge
- `Hermes.Wpf/Services/FuturesTerminalInstructions.cs` — протокол JSON для модели

---

## 1. Подготовка

### Сборка

```powershell
dotnet build Hermes.Wpf/Hermes.Wpf.csproj -c Debug
```

Сборка Wpf автоматически собирает и копирует `Hermes.BinanceDemoFuturesTerminal.exe` в output-папку Wpf.

### API-ключи

1. Binance → **Demo Trading** → **API Management**
2. В **Hermes Binance FUTURES DEMO Terminal** → «Настройки» → сохраните ключи
3. Без ключей snapshot будет без балансов, торговые команды вернут ошибку

### Запуск

1. `Hermes.BinanceDemoFuturesTerminal.exe` — кнопка **Binance Futures** в Hermes.Wpf или вручную
2. `Hermes.Wpf.exe`
3. Дождитесь загрузки баланса в futures-терминале

---

## 2. Проверка bridge (чтение данных)

PowerShell:

```powershell
# Терминал жив?
Get-Content "$env:LOCALAPPDATA\HermesFutures\bridge\heartbeat.txt"

# Есть секция FuturesTerminal?
Get-Content "$env:LOCALAPPDATA\HermesTrading\bridge\snapshot.json" |
  Select-String "FuturesTerminal" -Context 0,20
```

В `FuturesTerminal` должны быть:

- `HasCredentials: true`
- `Balances` (USDT)
- `Positions`, `OpenOrders`
- `SelectedSymbol`, `LastPrice`, `WsStatus`, `ChartInterval`

Если heartbeat есть, а snapshot пуст — подождите 5–10 с после загрузки аккаунта.

---

## 3. Режим трейдинга в Hermes.Wpf

1. Включите режим: напишите **«трейдинг»** / **«trading»** или выберите роль **Trader**
2. При включении futures-терминал автозапускается (`FuturesTerminalAutoLaunch = true` по умолчанию)
3. В исходящий промпт попадает блок **«Binance Demo Futures Terminal snapshot»**

### Команды простым языком (без JSON)

Hermes.Wpf понимает фразы **локально**, без вызова LLM:

| Фраза | Действие |
|-------|----------|
| «открой лонг по биткоину по рынку» | market BUY BTCUSDT |
| «открой шорт ETH 0.05 по рыночной» | market SELL ETHUSDT |
| «закрой позицию по биткоину» | close_position BTCUSDT |

В чате два сообщения:
1. **Команда отправлена на сервер Binance Demo Futures: …**
2. **Результат (Binance Demo Futures): …**

Если цена не указана («открой лонг по биткоину» без «по рынку»), Hermes спросит: «по рынку» / лимит / «нет».

### Локальные запросы (без LLM)

Быстрая проверка чтения snapshot:

| Запрос | Ожидание |
|--------|----------|
| «Какой баланс?» | Ответ с USDT free/locked |
| «Покажи позиции» | Список открытых позиций |
| «Сводка аккаунта» | Балансы + позиции + ордера |

Ответ приходит сразу из bridge, без вызова `hermes chat`.

---

## 4. Торговые команды через агента

В режиме трейдинга модель возвращает JSON — Hermes.Wpf исполняет его через очередь команд.

### Примеры JSON

**Market BUY:**

```json
{"skill":"trading","market":"futures","action":"place_order","symbol":"BTCUSDT","side":"BUY","order_type":"MARKET","quantity":0.001}
```

**Limit SELL:**

```json
{"skill":"trading","market":"futures","action":"place_order","symbol":"BTCUSDT","side":"SELL","order_type":"LIMIT","quantity":0.001,"price":95000}
```

**Закрыть позицию:**

```json
{"skill":"trading","market":"futures","action":"close_position","symbol":"BTCUSDT","order_type":"MARKET"}
```

**Закрыть все позиции:**

```json
{"skill":"trading","market":"futures","action":"close_all_positions"}
```

**Плечо:**

```json
{"skill":"trading","market":"futures","action":"set_leverage","symbol":"BTCUSDT","leverage":10}
```

**Отмена ордера:**

```json
{"skill":"trading","market":"futures","action":"cancel_order","symbol":"BTCUSDT","order_id":"12345678"}
```

### Поддерживаемые action

| action | Описание |
|--------|----------|
| `place_order` | MARKET / LIMIT, `quantity` в контрактах |
| `cancel_order` | `order_id` + `symbol` |
| `close_position` | reduce-only закрытие по symbol |
| `close_all_positions` | market close всех позиций |
| `set_leverage` | `leverage` (1–125, лимиты Binance) |

Поле `"market":"futures"` направляет команду в futures-bridge. `"market":"spot"` — в SpotTerminal (если включён).

### Ожидаемое поведение

1. В чате: `[trading] Order ...` или `[trading] Ошибка: ...`
2. В логах Hermes.Wpf: `[futures-bridge] enqueued ...` → `result ...`
3. В futures-терминале: обновление позиций / ордеров
4. На диске: `result-{guid}.json` в `%LocalAppData%\HermesFutures\bridge\`

---

## 5. Ручная проверка очереди (без агента)

Futures-терминал опрашивает `commands.json` раз в секунду:

```powershell
$cmd = @{
  Pending = @(
    @{
      Id = [guid]::NewGuid()
      CreatedUtc = (Get-Date).ToUniversalTime().ToString("o")
      Action = "place_order"
      Symbol = "BTCUSDT"
      Side = "BUY"
      OrderType = "MARKET"
      Quantity = 0.001
      RequestedBy = "manual-test"
    }
  )
} | ConvertTo-Json -Depth 5

$cmd | Set-Content "$env:LOCALAPPDATA\HermesFutures\bridge\commands.json"
```

Через 1–2 с проверьте `result-*.json` и UI терминала.

---

## 6. Логи

| Где | Что искать |
|-----|------------|
| Hermes.Wpf (вкладка Терминал) | `[futures-bridge]` |
| `Logs/Hermes.BinanceDemoFuturesTerminal/` | `[bridge] execute`, `[bridge] result` |
| REST в futures-терминале | `[REST] GET /fapi/v2/account`, `PlaceOrder` |

---

## 7. Минимальный сценарий за 2 минуты

1. Запустить **Binance Futures** → настроить ключи → дождаться баланса
2. Запустить **Hermes.Wpf** → написать **«трейдинг»**
3. Спросить: **«Какой баланс на futures demo?»** — ответ из snapshot
4. Спросить: **«Открой market buy 0.001 BTCUSDT на futures demo»** — агент вернёт JSON, Hermes исполнит ордер

---

## 8. Типичные проблемы

| Симптом | Решение |
|---------|---------|
| «Binance Demo Futures Terminal не запущен» | Запустите exe, проверьте `heartbeat.txt` |
| «API-ключи не настроены» | Ключи в настройках futures-терминала |
| Команда не исполняется | Окно futures-терминала должно быть открыто |
| Snapshot пуст | Подождите refresh аккаунта; проверьте ключи |
| `[trading] Ошибка: ...` | Смотрите `result-*.json` и лог futures-терминала |
| Режим трейдинга не активен | «трейдинг» / роль Trader |
| exe не найден | Пересоберите `Hermes.Wpf` |

---

## 9. Настройки Hermes.Wpf (settings.json)

| Поле | По умолчанию | Назначение |
|------|--------------|------------|
| `FuturesTerminalIntegrationEnabled` | `true` | Snapshot в промпт вне режима трейдинга |
| `FuturesTerminalAutoLaunch` | `true` | Автозапуск exe при stale heartbeat |
| `FuturesTerminalExePath` | пусто | Кастомный путь к exe |
| `TradingModeEnabled` | `false` | Режим трейдера + JSON-команды |

См. также: `Docs/Report/Hermes_Trading_Platform_Integration.md` (legacy TradingPlatform bridge).
