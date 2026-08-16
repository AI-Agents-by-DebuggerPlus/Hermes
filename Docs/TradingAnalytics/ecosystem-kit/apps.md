# Приложения и проекты (трейдинг)

Краткая карта. Детали — в репозитории Hermes (пути ниже в форме WSL `/mnt/d/...`).

## Проекты агента (`HermesProjects/`)

| Проект | Роль |
|--------|------|
| **Trading Analytics** (этот) | Индекс + аналитика; читать `ecosystem/` по необходимости |
| **Mt5Terminal** | Агент → JSON → HWT/MT5 (whitelist IPC) |
| **Mt5Developer** | Разработка EA / UI HWT |
| **TestTradingPlatform** | Песочница paper / эксперименты |

## Приложения (exe)

| Приложение | Назначение | Как задействовать |
|------------|------------|-------------------|
| **Hermes.BinanceDemoFuturesTerminal** | USDT-M Futures Demo, risk, bridge | Режим трейдинга в Command Center / кнопка Binance Futures |
| **Hermes.BinanceDemoSpotTerminal** | Spot Demo | Кнопка Binance Spot |
| **HermesWpfTerminal (HWT)** | UI к MT5, скрин графика, ордера UI | Проект Mt5Terminal + MT5 с EA |
| **Hermes.RemoteTerminal** | Удалённый просмотр статуса/скрина HWT | Supabase `hwt_*` сообщения |
| **Density Screener** (Python) | Стенки стакана / volume profile → JSON | `Source/ClaudeDensityScreener/` → см. `howto-density.md` |

## Документация в репо Hermes (не копировать сюда целиком)

- `/mnt/d/Programming/AI_Agents/Hermes/Docs/Reports/TradingModeReport/README.md` — режим трейдинга WPF↔Futures
- `/mnt/d/Programming/AI_Agents/Hermes/Docs/Reports/HermesWpfTerminal/README.md` — HWT / IPC / screenshot
- `/mnt/d/Programming/AI_Agents/Hermes/Docs/Reports/RemoteTerminal/HWT_HRT_Supabase_Protocol.md` — HWT↔RemoteTerminal
- `/mnt/d/Programming/AI_Agents/Hermes/Docs/ClaudeDensityScreener/start.txt` — архитектура плотностей

Windows-эквивалент корня: `D:\Programming\AI_Agents\Hermes\`.
