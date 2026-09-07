# Trading Analytics — карточка торгового сигнала

Когда анализ даёт **конкретный план сделки** (entry / SL / TP), оформи сигнал отдельным JSON-блоком **в конце** ответа (после `[info]` и HTML/инструкций).

## Формат (обязательно)

```json
{
  "skill": "trade_signal",
  "signal_id": "<uuid или короткий id>",
  "symbol": "XAUUSD",
  "side": "buy",
  "order_type": "limit",
  "entry": 4370.0,
  "entry_high": 4375.0,
  "stop_loss": 4345.0,
  "take_profit": 4500.0,
  "lot": 0.01,
  "timeframe": "H1",
  "summary": "Кратко: swing buy на коррекции",
  "visualization": "hermes/screenshots/plan.html"
}
```

| Поле | Обяз. | Примечание |
|------|-------|------------|
| `skill` | да | всегда `"trade_signal"` |
| `symbol` | да | из whitelist Mt5Terminal (XAUUSD, EURUSD, …) |
| `side` | да | `buy` / `sell` |
| `order_type` | да | `limit`, `stop`, `stop_limit` |
| `entry` | да | цена входа (или низ зоны) |
| `entry_high` | нет | верх зоны входа |
| `stop_loss`, `take_profit` | да для сигнала | числа |
| `lot` | желательно | иначе лот HWT |
| `visualization` | нет | HTML в `hermes/screenshots/` + skill `open-local-artifact` |

## Поведение Hermes.Wpf

1. Развёрнутый текст + визуализация — как сейчас.
2. JSON `trade_signal` **не показывается** в чате — парсится WPF.
3. Всплывающее окно **Apply / Cancel**.
4. Apply → IPC `place_pending` в **Mt5Terminal** (график должен быть того же символа).

## Запрещено

- Писать «ордер выставлен» — исполнение только после Apply пользователя.
- Символы вне whitelist Mt5Terminal.
- Смешивать с `{"skill":"trading"}` (Binance bridge).
