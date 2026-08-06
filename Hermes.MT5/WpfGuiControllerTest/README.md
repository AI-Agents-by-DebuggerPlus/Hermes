# MQL5 ⇄ HermesWpfGuiController.dll ⇄ HermesWpfTerminal (HWT)

Расположение: `Hermes.MT5/WpfGuiControllerTest/`

**HWT** = **HermesWpfTerminal** — WPF-терминал над MT5.  
Полное описание (агент-роутер, IPC, Real trading): [Docs/Reports/HermesWpfTerminal/README.md](../../Docs/Reports/HermesWpfTerminal/README.md).

## Окно

Класс Window: **`HermesWpfTerminal`** (`InpWpfWindow`).

- Account / Bid·Ask / лот / BUY·SELL
- Вкладки market / limit / stop / stop-limit
- **POSITIONS** + Close / Close all
- Settings: Real trading, Auto-trade
- Статус рынка, лог WPF/MQL5
- Agent IPC (`TerminalAgentIpc`) → `Mt5Terminal/hermes/ipc/`

## Сборка

```bat
cd Hermes.MT5\WpfGuiControllerTest\WpfGuiController
dotnet build -c Release

cd ..\WpfTestApp
dotnet build -c Release -p:OutputPath=bin\Release\ui_v33\
```

Актуальный `ui_vN` / `InpWpfUiN` — в `HermesProjects/Mt5Terminal/hermes/project.md`.

Если `bin\Release\net48` занят MT5 — используйте отдельный `OutputPath` (`ui_vN`).

## MT5

MetaEditor компилирует из:

`C:\Program Files\MetaTrader 5\MQL5\Experts\MyExperts\Hermes\`

```powershell
cd Hermes.MT5\WpfGuiControllerTest
.\Deploy-To-MT5.ps1
```

Затем: MetaEditor → F7 → снять EA → накинуть → **Reset** inputs  
(`InpWpfWindow = HermesWpfTerminal`, `InpWpfUi33` = путь к UI DLL).

## Торговля

- **Real trading ON** + Algo Trading в MT5 → `CTrade` / `PositionClose`
- OFF → только `ACK stub` в логе
- Успех: `FILLED` / `CLOSE ALL done`; ошибка: `ERR …`
- Агент Hermes управляет HWT только через JSON-роутер + IPC (не напрямую MT5)

## Agent IPC

Каталог: `HermesProjects/Mt5Terminal/hermes/ipc/`

| Файл | Кто пишет |
|------|-----------|
| `command.json` | Hermes.Wpf (после JSON роутера) |
| `result.json` / `status.json` | HWT |

HWT на команду делает те же клики UI, что и пользователь (`btnCloseAllPositions`, BUY/SELL, …).
