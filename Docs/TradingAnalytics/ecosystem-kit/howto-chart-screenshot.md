# Как получить скриншот графика (MT5 / HWT)

Используй **существующий** контур Mt5Terminal → Hermes.Wpf → HWT, не изобретай новый захват.

## Когда применять

Пользователь просит «скрин графика», «что на графике», «покажи MT5», анализ ситуации по картинке терминала.

## Порядок

1. Убедись, что открыт/доступен проект **Mt5Terminal** и HWT поднят из MT5 (иначе честно скажи, что терминал не запущен).
2. Для роутера агента — **один JSON** из whitelist (см. `Mt5Terminal/AGENTS.md`), типично:
   - `{"action":"screenshot"}` или `chart_screenshot`
3. Hermes.Wpf пишет IPC → HWT делает `ChartScreenShot` → путь в `result.json` / чат.
4. Картинку можно приложить к ответу (путь к PNG). Повтор последнего скрина — фразы вроде «повтор скриншота» (без нового CLI-цикла, если так настроено в WPF).

## Не путать

| Задача | Инструмент |
|--------|------------|
| График MT5 | HWT screenshot (этот howto) |
| Весь рабочий стол Windows | desktop capture / vision skill (другой контур) |
| Браузерный сайт | `$HERMES_SCREENSHOT_DIR` / browser tool |

Документация: `/mnt/d/Programming/AI_Agents/Hermes/Docs/Reports/HermesWpfTerminal/README.md`.
