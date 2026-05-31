# Hermes.Wpf ↔ Hermes Trading Platform

## Режим трейдинга (persona)

| Команда | Действие |
|---------|----------|
| `трейдинг` / `trading` | Включить режим трейдера-исполнителя (persist в settings) |
| `режим агента` | Вернуться в общий режим помощника |
| Торговая задача в общем режиме | Hermes.Wpf спрашивает: переключиться? (`да` / `трейдинг` / `нет`) |

В режиме трейдинга в промпт попадают snapshot терминала и JSON `skill:trading`. В режиме агента — только краткое правило «не торговать без переключения».

## Назначение

Пользователь общается с **Hermes.Wpf** (WSL `hermes chat`). При включённой интеграции клиент:

1. Подмешивает в исходящий промпт **live snapshot** терминала (баланс, позиции, ордера, риск, стратегии).
2. Распознаёт в ответе модели JSON `{"skill":"trading",...}` и ставит команды в очередь bridge.
3. Ждёт результат через **Hermes.TradingPlatform.Cli** (`enqueue` + `wait-result`).

Торговая логика остаётся в **Hermes.TradingPlatform.exe** (virtual exchange + RiskValidator). Hermes.Wpf не обходит риск.

## Требования

- Запущен **Hermes.TradingPlatform.exe** (публикует `snapshot.json` и `heartbeat.txt`).
- В Settings Hermes.Wpf: **«Интеграция Trading Platform»** включена (по умолчанию да).
- Собран **Hermes.TradingPlatform.Cli** (копируется в папку Hermes.Wpf при сборке Wpf, если Cli уже собран).

Bridge: `%LocalAppData%\HermesTrading\bridge\`

| Файл | Назначение |
|------|------------|
| `snapshot.json` | Состояние терминала |
| `commands.json` | Очередь команд от Wpf/CLI |
| `heartbeat.txt` | ISO timestamp, свежесть &lt; 12 с |
| `result-{guid}.json` | Результат команды |

## JSON из чата (модель → Wpf)

| action | Поля |
|--------|------|
| `query` | Только запрос статуса (snapshot уже в промпте) |
| `place_order` | `symbol`, `side` (Buy/Sell), `order_type` (Market/Limit/Stop), `quantity`, `price`, `reduce_only` |
| `cancel_order` | `order_id` |
| `enable_strategy` | `strategy_id` (liq-sweep, momentum, mean-rev), `enabled` |
| `emergency_stop` | — |

Пример рыночного ордера:

```json
{"skill":"trading","action":"place_order","symbol":"BTCUSDT","side":"Buy","order_type":"Market","quantity":0.01,"price":0,"reduce_only":false}
```

## CLI

```powershell
Hermes.TradingPlatform.Cli.exe status
Hermes.TradingPlatform.Cli.exe is-running
Hermes.TradingPlatform.Cli.exe enqueue '{"action":"place_order",...}'
Hermes.TradingPlatform.Cli.exe wait-result <guid> --timeout=20
```

## Алгоритмы

- **Стратегии** (Momentum, Mean Reversion, Liquidity Sweep): `enable_strategy` → `StrategyRunner` в терминале.
- **Orchestration** (страница Hermes в терминале): rule-based мониторинг, без прямых ордеров из orchestrator; чат может включать стратегии и ставить ручные ордера через bridge.

## Логи Hermes.Wpf

Корень: `D:\Programming\AI_Agents\Hermes\Logs` (или переменная `HERMES_LOGS_ROOT`).

**Hermes.Wpf:** `Logs\Hermes.Wpf\{project}\` — для каждого проекта подпапка (имя вкладки):

**Hermes.TradingPlatform:** `Logs\Hermes.TradingPlatform\trading_session_*.log` (bridge, exchange); `trade_journal_*.jsonl` (исполнения, баланс, realized PnL). В UI — страница **Journal**.

Для каждого проекта Wpf — подпапка (имя вкладки проекта):

| Файл | Содержимое |
|------|------------|
| `hermes_session_{yyyyMMdd_HHmmss}.log` | INFO/TERM/WARN, connection, bridge |
| `chat_{yyyyMMdd_HHmmss}.log` | Транскрипт чата (User/Hermes) |

Без выбранного проекта — папка `_session`.

Кнопка **Терминал** в Hermes.Wpf и авто-запуск при открытии чата / входе в режим трейдинга.

Закрытие позиции: `{"skill":"trading","action":"close_position","symbol":"ETHUSDT"}` (не `place_order` с Buy для шорта).

**Ручная торговля в терминале (без агента):** страницы **Positions** (Long/Short market, Закрыть / Закрыть все), **Orders** (любые типы ордеров), **Market Watch** (Long/Short/Close по строке).

**Сохранение сессии (перезапуск):** `%LocalAppData%\HermesTrading\session-state.json` — баланс, equity, PnL, позиции, ордера, журнал сделок, тикеры, стратегии. Доп. копия журнала: `trade_journal.jsonl`. Звуки сделок: **Settings → Звуки сделок**.

## Сборка

```powershell
dotnet build Hermes.TradingPlatform\Hermes.TradingPlatform.sln -c Release
dotnet build Hermes.Wpf\Hermes.Wpf.csproj -c Release
```

Запуск терминала:

```powershell
.\Hermes.TradingPlatform\Hermes.TradingPlatform.Wpf\bin\Release\net8.0-windows\Hermes.TradingPlatform.exe
```
