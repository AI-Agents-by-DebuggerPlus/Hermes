#Requires -Version 5.1
# Build HermesWpfTerminal v41 into ui_v41/ and deploy mq5 to MetaTrader.
param(
    [switch]$SkipMt5Deploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$uiDir = Join-Path $root "WpfTestApp\bin\Release\ui_v41"
$ctrlProj = Join-Path $root "WpfGuiController\WpfGuiController.csproj"
$uiProj = Join-Path $root "WpfTestApp\WpfTestApp.csproj"

Write-Host "=== Deploy HWT v41 ===" -ForegroundColor Cyan

Write-Host "dotnet build WpfGuiController (Release)..."
& dotnet build $ctrlProj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "WpfGuiController build failed" }

New-Item -ItemType Directory -Force -Path $uiDir | Out-Null
Write-Host "dotnet build WpfTestApp -> ui_v41..."
& dotnet build $uiProj -c Release -p:OutputPath="bin\Release\ui_v41\" --nologo
if ($LASTEXITCODE -ne 0) { throw "WpfTestApp build failed" }

$dll = Join-Path $uiDir "HermesWpfTerminalUi41.dll"
if (-not (Test-Path $dll)) { throw "Missing $dll" }

$info = Get-Item $dll
Write-Host "OK: $($info.FullName)" -ForegroundColor Green
Write-Host "Built: $($info.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"

if (-not $SkipMt5Deploy) {
    & (Join-Path $root "Deploy-To-MT5.ps1")
}

Write-Host ""
Write-Host "Next in MT5:" -ForegroundColor Yellow
Write-Host "  1. MetaEditor -> HermesWpfGuiControllerTest.mq5 -> F7"
Write-Host "  2. Chart: remove EA -> attach EA -> Inputs -> Reset"
Write-Host "  3. InpWpfUi41 = $dll"
Write-Host "  4. Close old HermesWpfTerminal window; EA opens v41 on attach"
