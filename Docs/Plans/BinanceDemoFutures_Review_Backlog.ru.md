# Binance Demo Futures Terminal — замечания и план исправлений

> **Статус:** только план, **изменения в код не вносить** до подтверждения пользователем.  
> **Дата:** 2026-05-30  
> **Скриншоты:** `Images/FuturesTeminal/Screenshot_27.png`, `Screenshot_28.png`

---

## Замечания (исходные)

### 1. Пропали условные операции с подсказками

**Скриншот:** `Screenshot_27.png` (окно настроек / общий UI).

**Наблюдение:** В форме ордера (`MainWindow.xaml`) сейчас только вкладки **«Лимит»** и **«Рынок»**. Вкладка **«Условные»** и связанный UI (стоп-цена, тип исполнения, working type, reduce only, TIF, иконка ℹ с popup-подсказкой) **отсутствуют в XAML**, хотя логика в `MainViewModel` и `BinanceApiService` (STOP / STOP_MARKET, `ExecuteConditionalOrderAsync`, `ConditionalOrderInfo`, `IsConditionalInfoOpen`) **сохранена**.

**Дополнительное требование:** таким же образом (иконка ℹ → popup по клику, не hover) реализовать подсказки для полей **риск-менеджера** в `SettingsWindow.xaml`:
- Макс. номинал ордера (USDT)
- Макс. суммарная экспозиция (USDT)
- Макс. открытых позиций
- Макс. плечо (проверка)

---

### 2. Агент Hermes должен выбирать объём согласно риск-менеджеру

**Наблюдение:** Сейчас объём задаётся:
- локальным парсером — фиксированные дефолты (`BTCUSDT=0.01` контракта в `TradingManualOrderParser`);
- агентом — поле `quantity` в JSON без привязки к `MaxOrderUsdt`, `MaxTotalExposureUsdt`, доступному балансу;
- bridge snapshot **не содержит** настроек риск-менеджера — агент их не видит в промпте.

Риск-менеджер в терминале (`RiskManager.ValidateOrder`) проверяет **номинал в USDT**, но не помогает **рассчитать** безопасный объём.

---

### 3. В истории сделок непонятно, в чём измеряется PnL

**Скриншот:** `Screenshot_28.png` (вкладка «ИСТОРИЯ СДЕЛОК»).

**Наблюдение:** Колонка **«Количество»** показывает `739.4 USDT`, **«Комиссия»** — `… USDT`, а **«Реализ. PnL»** — голое число `0.50300000` без единицы.

Код: `UserTradeModel.RealizedPnlDisplay` → `RealizedPnl.ToString("N8")` без суффикса `USDT`  
(`Models/AccountModels.cs`). Binance API поле `realizedPnl` для USDT-M futures — **в USDT**.

---

### 4. После закрытия позиций — сообщить в чат реальный PnL в USDT

**Требование:** После `close_position` / «закрой позицию…» Hermes.Wpf должен в чат вывести **фактический реализованный PnL** по закрытой позиции в **USDT**.

**Наблюдение:** Сейчас `TradingExecutionMessages.FormatCommandResult` показывает только текст от bridge (`Order … NEW`), без PnL.  
Источник данных: `GET /fapi/v1/userTrades` (`realizedPnl` на fill) или `GET /fapi/v1/income` (type=REALIZED_PNL) — API уже частично используется в `GetUserTradesAsync`.

---

### 5. Объём в USDT при открытии/закрытии и в результатах исполнения

**Требование:** В чате Hermes и в уведомлениях терминала объём указывать **в USDT** (номинал), а не только в контрактах.

**Наблюдение:**
- `TradingExecutionMessages.DescribeAction` — `объём {cmd.Quantity}` без единицы и без пересчёта в USDT;
- bridge `ExecuteBridgePlaceOrderAsync` логирует контракты;
- форма ордера уже умеет режим **USDT** (`QuantityInputMode.UsdtOrderSize`), но agent/bridge path работает в **контрактах**.

---

## План исправлений

### Пункт 1 — Восстановить «Условные» + подсказки риск-менеджера

| Шаг | Действие | Файлы |
|-----|----------|-------|
| 1.1 | Вернуть вкладку **«Условные»** в форму ордера (RadioButton `IsConditionalOrderEntry`) | `MainWindow.xaml` |
| 1.2 | Показать поля условного ордера при `IsConditionalOrder`: стоп-цена, Limit/Market после триггера, working type, reduce only, TIF | `MainWindow.xaml` |
| 1.3 | Иконка **ℹ** + Popup по **клику** с текстом `ConditionalOrderInfo` + ссылка «Подробнее» (`OpenConditionalInfoLinkCommand`) | `MainWindow.xaml`, стили (как было: `DarkLinkBtnStyle`, popup) |
| 1.4 | Переиспользовать паттерн popup для полей риск-менеджера: рядом с каждым label — ℹ, текст подсказки (что означает лимит, как влияет на ордер) | `SettingsWindow.xaml`, опционально `SettingsViewModel` (строки-подсказки) |
| 1.5 | Проверить тёмный hover для кнопок popup (правило `.cursor/rules/dark-ui-hover.mdc`) | глобальные стили |

**Критерий готовности:** UI «Условные» функционален end-to-end; подсказки риск-менеджера открываются по клику и понятны без документации.

**Оценка:** ~4–6 ч (XAML + стили + ручная проверка).

---

### Пункт 2 — Объём ордера по риск-менеджеру (агент + локальный парсер)

| Шаг | Действие | Файлы |
|-----|----------|-------|
| 2.1 | Добавить секцию **RiskSettings** в bridge snapshot: `MaxOrderUsdt`, `MaxTotalExposureUsdt`, `MaxOpenPositions`, `MaxLeverage`, `RiskManagementEnabled`, доступный USDT | `FuturesTerminalSnapshotSection`, `MainViewModel.BuildBridgeSnapshot`, `FuturesTerminalBridgeService.BuildFuturesContextBlockRu` |
| 2.2 | Обновить `FuturesTerminalInstructions` / `TradingModePromptDefaults`: если пользователь не указал объём — рассчитать **номинал ≤ MaxOrderUsdt**, учесть текущую экспозицию и баланс; в JSON передавать `quantity_usdt` или `quantity` с явной семантикой | `Hermes.Wpf/Services/*.cs` |
| 2.3 | Расширить протокол bridge: поле `QuantityUsdt` (или action-level flag) → терминал конвертирует USDT → контракты по mark price / last price | `FuturesPlatformCommand`, `MainViewModel.Bridge.cs`, `FuturesTradingCommandExecutor` |
| 2.4 | Локальный парсер: если объём не указан — `min(MaxOrderUsdt, available * k)` вместо фиксированных 0.01 BTC | `TradingManualOrderParser`, новый helper `RiskBasedQuantityCalculator` (читает snapshot/settings) |
| 2.5 | Валидация на стороне терминала остаётся (`RiskManager.ValidateOrder`) — agent не должен предлагать объём выше лимитов | без изменений логики, только согласованность |

**Критерий готовности:** «открой лонг по биткоину по рынку» без объёма → ордер с номиналом ≤ `MaxOrderUsdt` (500 USDT по умолчанию); агент видит лимиты в snapshot.

**Оценка:** ~6–8 ч (snapshot + протокол + парсер + инструкции + тесты).

---

### Пункт 3 — Единицы PnL в истории сделок

| Шаг | Действие | Файлы |
|-----|----------|-------|
| 3.1 | `RealizedPnlDisplay` → формат с суффиксом **USDT** и знаком (+/−), как у `QuantityDisplay` | `Models/AccountModels.cs` |
| 3.2 | Заголовок колонки: **«Реализ. PnL (USDT)»** | `Controls/TradeHistoryPanel.xaml` |
| 3.3 | Опционально: цвет строки (зелёный/красный) по знаку PnL | `TradeHistoryPanel.xaml` DataTemplate |

**Критерий готовности:** в таблице явно видно, что PnL в USDT.

**Оценка:** ~30 мин.

---

### Пункт 4 — Реальный PnL в чат после закрытия позиции

| Шаг | Действие | Файлы |
|-----|----------|-------|
| 4.1 | После успешного `close_position` / `close_all_positions`: запрос `userTrades` за последние N секунд по symbol (или diff realizedPnl до/после) | `BinanceApiService`, `MainViewModel.Bridge.cs` или post-hook в `FuturesTradingCommandExecutor` |
| 4.2 | Суммировать `realizedPnl` по fills закрытия; вернуть в `FuturesPlatformCommandResultFile.Message` или отдельное поле `RealizedPnlUsdt` | bridge DTO |
| 4.3 | `TradingExecutionMessages.FormatCommandResult` для close: **«Реализованный PnL: +12.34 USDT»** | `Hermes.Wpf/Services/TradingExecutionMessages.cs` |
| 4.4 | Для `close_all_positions` — список по символам или итог | тот же слой |

**Критерий готовности:** после «закрой позицию по биткоину» второе сообщение в чате содержит реальный PnL в USDT с Binance Demo API.

**Оценка:** ~4–5 ч (API polling, race conditions, агрегация fills).

---

### Пункт 5 — Объём в USDT в чате и уведомлениях

| Шаг | Действие | Файлы |
|-----|----------|-------|
| 5.1 | `TradingExecutionMessages`: для place/close показывать **«номинал X.XX USDT»** (qty × price или quoteQty из ответа API) | `TradingExecutionMessages.cs` |
| 5.2 | Bridge result message: включать `executedQty` + **notional USDT** из ответа `PlaceOrderAsync` | `MainViewModel.Bridge.cs` |
| 5.3 | Локальный парсер: если пользователь пишет «на 100 usdt» — парсить USDT, не контракты | `TradingManualOrderParser` |
| 5.4 | Инструкции агента: `quantity_usdt` предпочтительнее `quantity` (контракты) | `FuturesTerminalInstructions.cs` |
| 5.5 | Согласовать с п.2: единая модель «объём всегда в USDT в UI чата» | cross-cutting |

**Критерий готовности:** сообщения «отправлено» / «результат» содержат объём в USDT; пользователь не видит «0.001» без контекста.

**Оценка:** ~3–4 ч (зависит от п.2).

---

## Рекомендуемый порядок работ

```
П3 (быстрый UX-fix PnL в таблице)
  ↓
П1 (UI условных + подсказки риска — блокирует полноценную торговлю)
  ↓
П5 (USDT в сообщениях — частично параллельно с П2)
  ↓
П2 (риск-based объём для агента)
  ↓
П4 (PnL после закрытия — зависит от стабильного close flow)
```

---

## Зависимости между пунктами

| | П1 | П2 | П3 | П4 | П5 |
|---|:---:|:---:|:---:|:---:|:---:|
| **П1** | — | | | | |
| **П2** | | — | | | ↔ П5 |
| **П3** | | | — | | |
| **П4** | | | | — | ↔ П5 |
| **П5** | | ↔ П2 | | ↔ П4 | — |

---

## Риски и открытые вопросы (на согласование)

1. **П2 — дефолтный объём без указания пользователем:** использовать 100% `MaxOrderUsdt` или процент от доступного баланса (например 25%)?
2. **П4 — задержка API:** `userTrades` может прийти с задержкой 1–3 с — нужен poll/retry в bridge result wait?
3. **П1 — STOP_MARKET / Algo API:** ранее возможна ошибка Binance `-4120` для условных ордеров — проверить при восстановлении UI.
4. **П5 — контракты vs USDT в JSON:** оставить оба поля (`quantity` + `quantity_usdt`) или мигрировать только на USDT?

---

## Связанные файлы (справочно)

| Область | Путь |
|---------|------|
| Форма ордера | `Hermes.BinanceDemoFuturesTerminal/MainWindow.xaml` |
| Условные (VM) | `Hermes.BinanceDemoFuturesTerminal/ViewModels/MainViewModel.cs` |
| Риск-менеджер UI | `Hermes.BinanceDemoFuturesTerminal/Views/SettingsWindow.xaml` |
| Риск-логика | `Hermes.BinanceDemoFuturesTerminal/Services/RiskManager.cs` |
| История сделок | `Hermes.BinanceDemoFuturesTerminal/Controls/TradeHistoryPanel.xaml`, `UserTradeModel` |
| Agent bridge | `Hermes.Wpf/Services/FuturesTerminalBridgeService.cs`, `TradingExecutionMessages.cs` |
| NL-парсер | `Hermes.Wpf/Services/TradingManualOrderParser.cs` |

---

*Документ создан по запросу пользователя. Изменения в репозиторий не вносились.*
