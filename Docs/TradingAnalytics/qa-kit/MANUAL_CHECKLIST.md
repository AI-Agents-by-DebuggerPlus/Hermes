# Ручная / визуальная проверка (Trading Analytics)

**Кто оценивает:** пользователь глазами.  
**Как запускать приложения:** **Hermes.Wpf Launcher** → раздел **Testing** (не чат Trading Analytics).

После запуска приложений пройди пункты ниже. Автотесты: `qa/run_all_checks.ps1`.

## Density Screener

| # | Что сделать | Ожидание |
|---|-------------|----------|
| D1 | Launcher → Density Screener | Окно без traceback, периодические `snapshot written` |
| D2 | `python scripts\summarize_density.py` в каталоге screener | `STATUS: screener OK`, ≥1 level |
| D3 | Открыть `%LocalAppData%\HermesDensity\bridge\density_snapshot.json` | Валидный JSON: `symbol`, `current_price`, `levels[]` |
| D4 | Hermes.Wpf → проект **Trading Analytics** → «Какие плотности по BTC?» | Краткий ответ по levels / howto |

## Futures Demo

| # | Что сделать | Ожидание |
|---|-------------|----------|
| F1 | Launcher → Binance Demo Futures | Окно терминала, WS connected |
| F2 | `%LocalAppData%\HermesTrading\bridge\heartbeat.txt` | Обновляется (~секунды) |
| F3 | `snapshot.json` в том же bridge | Есть FuturesTerminal / цена / позиции |

## HWT / Project Manager

См. прежние пункты H* / P* при необходимости (MT5 + HWT / плитки PM).
