# MT5 — список доступных символов (один раз)

Trading Analytics **не опрашивает Mt5Terminal каждый turn**. WPF делает **один IPC-запрос**, кэширует результат в `hermes/mt5_symbols.json`.

## Когда нужен список

- Перед первым торговым сигналом (чтобы `symbol` совпадал с именем у брокера: NAS100 / USTEC / XAUUSDm).
- После смены брокера или счёта MT5.

## Как запросить (агент Trading Analytics)

1. Убедись, что **HermesWpfTerminal** открыт и на графике MT5 запущен EA `HermesWpfGuiControllerTest`.
2. В **конце ответа** (один раз за сессию или по запросу пользователя) добавь JSON:

```json
{"skill":"mt5_symbols","action":"fetch"}
```

Принудительное обновление кэша:

```json
{"skill":"mt5_symbols","action":"refresh"}
```

3. WPF выполнит `list_symbols` в Mt5Terminal, сохранит файл:

`hermes/mt5_symbols.json` — массив `symbols`, поле `chart_symbol`, `count`, `fetched_at_utc`.

4. Дальше **читай только кэш** (`read_file hermes/mt5_symbols.json`), не повторяй запрос без необходимости.

## Что делает Mt5Terminal

| action | IPC | EA |
|--------|-----|-----|
| `list_symbols` | `hermes/ipc/command.json` | `ExportSymbolsList()` → `hermes/ipc/symbols.json` |

Символы: все **торгуемые** в терминале (до 4000), не только символ текущего графика.

## Автозагрузка

При выборе проекта **Trading Analytics** WPF сам запросит список, если `hermes/mt5_symbols.json` отсутствует или старше 7 дней.

## Прямой запрос в Mt5Terminal

В чате проекта Mt5Terminal (режим роутера):

```json
{"id":"sym1","action":"list_symbols"}
```

Ответ — путь к `symbols.json` и количество символов.
