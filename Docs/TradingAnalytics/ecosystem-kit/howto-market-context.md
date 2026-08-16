# Рыночный контекст без нового ордера

Цель: ответить «что сейчас на рынке / в позициях», **не** открывая сделку.

## Источник 1 — Futures Demo snapshot

1. Прочитай `live-data.md` → `snapshot.json`.
2. Если heartbeat свежий — кратко: символ, last price, открытые позиции, плечо/лимиты риска.
3. Если heartbeat stale — скажи, что Demo Futures Terminal не жив; предложи запустить **Binance Demo Futures** из Command Center.

## Источник 2 — скрин графика MT5

Если нужен визуальный контекст MT5 — `howto-chart-screenshot.md`.

## Источник 3 — плотности стакана

Если вопрос про стенки / отскоки / ликвидность — `howto-density.md` (нужен запущенный screener).

## Ордера

Только по явной просьбе пользователя и через существующие контуры:

- Futures Demo → режим трейдинга / `skill:trading` + risk layers  
- MT5 → whitelist JSON Mt5Terminal  

Не обходи RiskManager и safety rules.
