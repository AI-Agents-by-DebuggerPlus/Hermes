# Hermes.StrategyViewer — план

**Дата:** 2026-08-23  
**Референс UI:** `Images/TradingAnalytics/Screenshot_1.png` (= `user_files/gold_strategy.html`)  
**Контекст логов:** Trading Analytics `chat_20260823_100636` — стратегия по золоту + open в браузере (статика).

## Цель

WPF-приложение **Hermes.StrategyViewer**: тот же визуальный язык, что у HTML агента (карточки сценариев / риск / техфон), но:

1. **Live** — подставляются актуальная цена и индикаторы.
2. **Интерактив** — подсветка условий сценария: подтверждено / отвергнуто / ожидание.
3. **Алерты** — на цене, уровне или значении индикатора → звук локально + Telegram.

Не дублирует исполнение ордеров (это Mt5Terminal / trade_signal). Viewer = мониторинг и оповещения.

---

## Что показали логи (2026-08-23)

| Время | Событие | Итог |
|--------|---------|------|
| 10:08–10:12 | «Отобрази в браузере» | `open-local-artifact` OK → `gold_analysis.html` |
| 10:17 | «Составь стратегию по золоту» | Текст: сценарии A/B/C, уровни, риск |
| 10:20 | «Отобрази стратегию в браузере» | `gold_strategy.html` создан + открыт |
| 10:24 | Источники данных | TradingView (browser), новости — **разовый scrape**, не live feed |

Проблема: HTML заморожен на момент генерации (`$4,602.99`, RSI 70.5). StrategyViewer закрывает разрыв **статика → live**.

---

## Архитектура

```
Trading Analytics (агент)
    │  write_file strategy JSON (+ optional HTML preview)
    ▼
HermesProjects/Trading Analytics/strategies/*.json
    │  file watch / IPC open
    ▼
Hermes.StrategyViewer (WPF)
    ├── UI: карточки как Screenshot_1 (dark JetBrains Mono)
    ├── LiveFeed: цена + индикаторы (см. источники)
    ├── ScenarioEngine: eval условий → Confirmed / Rejected / Pending
    └── AlertService: local beep/wav + Telegram Bot API
```

### Источники live-данных (приоритет)

| Приоритет | Источник | Что даёт |
|-----------|----------|----------|
| 1 | **HWT / Mt5Terminal IPC** | bid/ask, symbol — уже в экосистеме Trading Analytics |
| 2 | **Binance WS** (если символ мапится) | тикер, klines → RSI/SMA локально |
| 3 | Периодический REST/klines (fallback) | не poll для чата; только для viewer feed |

Индикаторы (RSI, Stoch, EMA): считать локально по свечам **или** читать snapshot, который агент/сервис кладёт рядом (`strategies/<id>.live.json`). Не зависеть от Playwright/TradingView в runtime viewer.

### Контракт стратегии (агент → файл)

Агент после «составь стратегию» пишет JSON (и по желанию HTML). Viewer читает JSON.

См. `strategy.schema.json` / пример `gold_xauusd.strategy.json`.

Ключевые поля:

- `symbol`, `timeframe`, `as_of`
- `scenarios[]`: `id`, `title`, `bias` (buy/sell), `entry`/`sl`/`tp`, `conditions[]`
- `conditions[]`: `kind` = `price_in_range` | `price_above` | `price_below` | `indicator_cmp` | `candle_close_above`
- `indicators[]`: `name`, `period`, `timeframe`, `warn_above` / `warn_below`
- `alerts[]`: уровни/условия + `channels: ["local","telegram"]`
- `risk`: текст / % / leverage (display)

### UI (как на скрине)

- Header: `STRATEGY: {SYMBOL}` + live price + last update
- Grid 2×2: Scenario A, Scenario B, Risk, Technical backdrop
- У каждого условия — статус-чип: 🟢 Confirmed / 🔴 Rejected / ⚪ Pending
- Карточка сценария целиком: border glow по «лучшему» статусу
- Панель Alerts: список + toggle mute

### Алерты

| Тип | Пример | Триггер |
|-----|--------|---------|
| Price level | SL $4380 | `price <= level` (или cross) |
| Price zone | Entry 4430–4450 | вход в диапазон |
| Indicator | RSI ≤ 50 | условие сценария A |
| Scenario flip | A → Confirmed | смена статуса |

**Local:** `System.Media.SystemSounds` / WAV из settings.  
**Telegram:** Bot token + chat_id в `credentials` / settings (не в git); debounce 60s на тот же alert id.

---

## Фазы реализации

### Phase 0 — контракт (сейчас)
- [x] Этот план
- [ ] JSON schema + пример из `gold_strategy.html`
- [ ] Правило в Trading Analytics `AGENTS.md`: после стратегии писать JSON + открывать Viewer (или HTML fallback)

### Phase 1 — Viewer shell (WPF)
- Отдельный проект `Hermes.StrategyViewer` (net8-windows)
- Загрузка JSON → отрисовка карточек (статичные значения из файла)
- Launcher → Testing кнопка
- Dark UI palette как Screenshot_1 / Hermes terminals

### Phase 2 — Live price
- Подписка на HWT quote (IPC) или Binance ticker
- Header price обновляется; distance % до entry/SL/TP

### Phase 3 — ScenarioEngine
- Eval conditions на live + индикаторы с klines
- Подсветка условий и сценариев

### Phase 4 — Alerts
- CRUD алертов в UI (из JSON + ручные)
- Local sound + Telegram
- Cooldown / mute

### Phase 5 — Agent integration
- Skill `open-strategy-viewer` (как open-local-artifact): `Start-Process` exe + path to JSON
- Trading Analytics: «отобрази стратегию» → JSON + Viewer, не только HTML

---

## Связь с экосистемой

| Компонент | Роль |
|-----------|------|
| Trading Analytics | Генерирует стратегию (текст + JSON) |
| Hermes.StrategyViewer | Live UI + алерты |
| Mt5Terminal / trade_signal | Исполнение (отдельно, Apply в WPF) |
| open-local-artifact | Fallback HTML preview |
| Hermes gateway Telegram | Можно переиспользовать bot token из `~/.hermes` **или** отдельный bot для алертов |

---

## Решение «с чего начать»

Рекомендуемый порядок: **Phase 0 schema + Phase 1 shell** (открыть `gold_*.strategy.json` как Screenshot_1), затем live price, затем алерты.

Не начинать с Telegram до появления live eval — иначе алерты на мёртвых данных.
