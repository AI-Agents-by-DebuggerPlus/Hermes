# Trading Analytics — kit экосистемы

Шаблоны копируются в папку проекта агента `HermesProjects/Trading Analytics/`:

| Kit | Куда |
|-----|------|
| `ecosystem-kit/` | `hermes/ecosystem/` |
| `qa-kit/` | `qa/` (автотесты + ручной чеклист) |

Назначение: **on-demand** чтение агентом (INDEX → один howto / QA), без раздувания `~/.hermes/` и без инъекции в каждый turn WPF.

Установка: автоматически при `EnsureProjectHermesArtifacts` для проекта с именем *Trading Analytics*, либо вручную скопировать kit → соответствующие папки.

QA: агент запускает `qa/run_all_checks.ps1`, затем даёт релевантные пункты из `qa/MANUAL_CHECKLIST.md`.

## HTML / визуализация в браузере

После создания `*.html` (анализ, p5.js, схема) агент **в том же turn** обязан открыть файл через skill **`open-local-artifact`** — см. `skills/open-local-artifact/SKILL.md` и блок «КРИТИЧНО» в `AGENTS.md` проекта.

Триггеры: «отобрази визуально в браузере», «покажи в браузере», «открой HTML».

Путь с пробелом `Trading Analytics` — только с кавычками в Chrome/Edge.

## StrategyViewer (live)

Статичный HTML стратегии (как `gold_strategy.html` / Screenshot_1) → живой мониторинг:

- План: [`Hermes_StrategyViewer_PLAN.md`](Hermes_StrategyViewer_PLAN.md)
- Schema: [`strategy.schema.json`](strategy.schema.json)
- Пример: [`examples/gold_xauusd.strategy.json`](examples/gold_xauusd.strategy.json)

Алерты: локальный звук + Telegram; исполнение ордеров — по-прежнему через `trade_signal` / Mt5Terminal.
