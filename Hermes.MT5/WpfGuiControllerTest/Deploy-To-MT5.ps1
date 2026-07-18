# Deploy HermesWpfGuiControllerTest.mq5 + controller DLL into the MT5 folder
# that MetaEditor actually compiles (Program Files portable layout).

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

$repoMq5 = Join-Path $root "MQL5\HermesWpfGuiControllerTest.mq5"
$mt5Experts = "C:\Program Files\MetaTrader 5\MQL5\Experts\MyExperts\Hermes"
$mt5Libs = "C:\Program Files\MetaTrader 5\MQL5\Libraries"
$ctrl = Join-Path $root "WpfGuiController\bin\Release\net48\HermesWpfGuiController.dll"

if (-not (Test-Path $repoMq5)) { throw "mq5 not found: $repoMq5" }

New-Item -ItemType Directory -Force -Path $mt5Experts | Out-Null
Copy-Item -Force $repoMq5 (Join-Path $mt5Experts "HermesWpfGuiControllerTest.mq5")
Write-Host "mq5 -> $mt5Experts"

if (Test-Path $ctrl) {
    New-Item -ItemType Directory -Force -Path $mt5Libs | Out-Null
    Copy-Item -Force $ctrl (Join-Path $mt5Libs "HermesWpfGuiController.dll")
    Write-Host "controller -> $mt5Libs"
} else {
    Write-Host "WARN: controller DLL not built yet: $ctrl"
}

Select-String -Path (Join-Path $mt5Experts "HermesWpfGuiControllerTest.mq5") -Pattern "input string InpWpfUi" |
    ForEach-Object { $_.Line.Trim() }
Write-Host "Done. In MetaEditor: reopen mq5, F7, reattach EA + Reset inputs."
