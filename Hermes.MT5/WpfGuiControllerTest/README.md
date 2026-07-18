# MQL5 ⇄ HermesWpfGuiController.dll ⇄ HermesWpfTerminal (Hermes.MT5)

Расположение: `Hermes.MT5/WpfGuiControllerTest/`

## Окно

Класс Window: **`HermesWpfTerminal`** (`InpWindowName`).

- сверху: валютная пара (`txtSymbol`)
- Bid / Ask, лот, BUY/SELL
- снизу: строка статуса рынка (`txtMarketStatus`) — открыт/закрыт, сессия,
  время до закрытия или до открытия (по `SymbolInfoSessionQuote`, fallback FX weekend UTC)

## Сборка

```bat
cd Hermes.MT5\WpfGuiControllerTest\WpfGuiController
dotnet build -c Release

cd ..\WpfTestApp
dotnet build -c Release
```

Если `bin\Release\net48` занят MT5 — сборка уходит в `bin\Release\net48_deploy\`
(путь по умолчанию в EA).

## MT5

MetaEditor компилирует отсюда (не из AppData):

`C:\Program Files\MetaTrader 5\MQL5\Experts\MyExperts\Hermes\`

После правок в репо:

```powershell
cd Hermes.MT5\WpfGuiControllerTest
.\Deploy-To-MT5.ps1
```

Затем в MetaEditor: открыть mq5 → F7 → снять EA → накинуть → Reset inputs.

```mql5
#import "HermesWpfGuiController.dll"
#import
GuiController::ShowWindow(path, "HermesWpfTerminal");
```

1. Снять EA с графика (разблокирует DLL)
2. Перекомпилировать EA в MetaEditor
3. Снова накинуть EA; в параметрах `InpWindowName = HermesWpfTerminal`
