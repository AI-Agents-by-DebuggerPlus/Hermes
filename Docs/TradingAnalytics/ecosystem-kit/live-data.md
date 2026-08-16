# Живые данные (file IPC)

Читай файлы **только когда** нужны актуальные цена / позиции / статус терминала.

## Binance Demo Futures (unified bridge)

| Файл | Смысл |
|------|--------|
| `%LocalAppData%/HermesTrading/bridge/snapshot.json` | Снимок терминала (цена, позиции, лимиты) |
| `%LocalAppData%/HermesTrading/bridge/heartbeat.txt` | Живость (UTC); stale если старше ~12 с |
| `%LocalAppData%/HermesFutures/bridge/commands.json` | Очередь команд WPF → терминал |
| `%LocalAppData%/HermesFutures/bridge/result-*.json` | Результат одной команды |

В WSL: `/mnt/c/Users/<you>/AppData/Local/HermesTrading/bridge/snapshot.json`  
(подставь своего пользователя Windows).

Не парси весь JSON в ответ пользователю — вытащи 3–6 ключевых полей (symbol, lastPrice, positions, risk caps).

## HWT / Mt5Terminal IPC

Каталог проекта Mt5Terminal: `…/HermesProjects/Mt5Terminal/hermes/ipc/`

| Файл | Смысл |
|------|--------|
| `command.json` | Команда агента → HWT |
| `result.json` | Факт исполнения |
| `status.json` | Снимок UI (если публикуется) |

Подробности: `howto-chart-screenshot.md`, doc HWT в `apps.md`.

## Density Screener

| Файл | Смысл |
|------|--------|
| `%LocalAppData%/HermesDensity/bridge/density_snapshot.json` | Уровни плотностей |
| `%LocalAppData%/HermesDensity/bridge/heartbeat.txt` | Живость screener (UTC ISO) |

См. `howto-density.md`. Не смешивать с `HermesTrading/bridge/snapshot.json`.
