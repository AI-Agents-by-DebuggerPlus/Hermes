# HermesWpfTerminal (HWT)

**Дата:** 2026-08-05  
**Код:** `Hermes.MT5/WpfGuiControllerTest/`  
**Проект агента:** `HermesProjects/Mt5Terminal/`

**HWT** — окно `HermesWpfTerminal`: WPF UI-терминал, который MetaTrader 5 поднимает через DLL-мост.  
Агент Hermes **не** ходит в MT5 напрямую: торговые операции только через HWT.

---

## Архитектура

```
Пользователь (чат Mt5Terminal)
    → Hermes CLI (только JSON-роутер из белого списка)
    → Hermes.Wpf (парсит stdout, пишет hermes/ipc/command.json)
    → HWT (клик UI / флаги Settings)
    → HermesWpfGuiController.dll (очередь событий)
    → EA HermesWpfGuiControllerTest.mq5 (CTrade / stub)
```

| Слой | Роль |
|------|------|
| Hermes CLI | Выбрать `action` из whitelist или `unsupported` |
| Hermes.Wpf | Исполнитель IPC + текст в чат = факт `result.json` |
| **HWT** | Единственный UI-фасад к торговле |
| EA | `OrderSend` / `PositionClose` при Real trading ON |

---

## Стек HWT

```
EA (.mq5)
  → #import HermesWpfGuiController.dll
  → Assembly.LoadFrom(HermesWpfTerminalUiN.dll)
  → окно HermesWpfTerminal
```

| Компонент | Путь |
|-----------|------|
| Bridge | `WpfGuiController/` → `HermesWpfGuiController.dll` |
| UI | `WpfTestApp/` → `ui_vN/HermesWpfTerminalUiN.dll` |
| EA | `MQL5/HermesWpfGuiControllerTest.mq5` |
| Deploy | `Deploy-To-MT5.ps1` |
| Agent IPC | `TerminalAgentIpc.cs` в UI; каталог `Mt5Terminal/hermes/ipc/` |
| Router (WPF) | `Hermes.Wpf/Services/Mt5TerminalTradeRouter.cs`, `Mt5TerminalIpcClient.cs` |

Актуальная версия UI и input EA — в `HermesProjects/Mt5Terminal/hermes/project.md` (сейчас **ui_v33** / `InpWpfUi33`).

---

## Возможности UI

- Котировки Bid/Ask, лот, BUY/SELL (market / pending tabs)
- **POSITIONS** — до 8 строк, **Close** / **Close all**
- Settings: **Real trading**, **Auto-trade** (tooltips)
- Статус рынка (сессии), лог WPF/MQL5
- Agent IPC: опрос `command.json`, ответ `result.json`, снимок `status.json`

### Real trading

| Флаг | Поведение |
|------|-----------|
| OFF | `ACK stub` — ордер/закрытие не уходит на рынок |
| ON | `CTrade` (`Buy`/`Sell`/pending/`PositionClose`) + нужен Algo Trading в MT5 |

---

## Роутер агента (белый список)

Агент отвечает **одним JSON** (см. `Mt5Terminal/AGENTS.md`):

| action | Смысл |
|--------|--------|
| `snapshot` | снимок UI |
| `set_real_trading` / `set_auto_trade` | флаги Settings |
| `set_lot` | лот |
| `buy_market` / `sell_market` | рыночные кнопки |
| `close_all` / `close_slot` | закрытие позиций |
| `unsupported` | запрос не из списка — без исполнения |

Hermes.Wpf:

1. Парсит JSON из stdout CLI  
2. При whitelist-задаче пишет IPC  
3. В чат кладёт результат HWT (не «рассказ» модели)  
4. При `unsupported` / отсутствии JSON — явный отказ

**Запрещено агенту:** писать в папки MT5/MQL5, симулировать сделки через `print`/`execute_code`.

---

## Сборка и деплой

```powershell
cd Hermes.MT5\WpfGuiControllerTest\WpfGuiController
dotnet build -c Release

cd ..\WpfTestApp
dotnet build -c Release -p:OutputPath=bin\Release\ui_v33\

cd ..
.\Deploy-To-MT5.ps1
```

Затем MetaEditor: **F7** → снять EA → накинуть → **Reset** inputs (`InpWpfUi33`, `InpWpfWindow=HermesWpfTerminal`).

При смене UI: новый `ui_vN` + `AssemblyName` + input EA + `BuildInfo` (кэш `Assembly.LoadFrom` в MT5).

Подробнее по путям MetaEditor: `.cursor/rules/mt5-wpf-gui-deploy.mdc`.

---

## Проверка «прошло через HWT»

В логе панели / `status.json` → `log_tail` должны быть строки вида:

```
[WPF]  IPC cmd id=… action=close_all
[MQL5] recv CLICK btnCloseAllPositions
[MQL5] SEND CLOSE ALL …   (или ACK stub, если Real trading OFF)
```

Наличие `recv CLICK btn…` = команда пришла из HWT, не из прямого доступа агента к MT5.

---

## Связанные файлы

- Модуль: [Hermes.MT5/WpfGuiControllerTest/README.md](../../Hermes.MT5/WpfGuiControllerTest/README.md)
- Правила агента: `HermesProjects/Mt5Terminal/AGENTS.md`
- IPC: `HermesProjects/Mt5Terminal/hermes/ipc/README.md`
