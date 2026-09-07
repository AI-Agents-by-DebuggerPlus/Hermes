#Requires -Version 5.1
param([switch]$SkipMt5Deploy)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$uiDir = Join-Path $root "WpfTestApp\bin\Release\ui_v47"
$ctrlProj = Join-Path $root "WpfGuiController\WpfGuiController.csproj"
$uiProj = Join-Path $root "WpfTestApp\WpfTestApp.csproj"
Write-Host "=== Deploy HWT v47 ===" -ForegroundColor Cyan
& dotnet build $ctrlProj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "WpfGuiController build failed" }
New-Item -ItemType Directory -Force -Path $uiDir | Out-Null
& dotnet build $uiProj -c Release -p:OutputPath="bin\Release\ui_v47\" --nologo
if ($LASTEXITCODE -ne 0) { throw "WpfTestApp build failed" }
$dll = Join-Path $uiDir "HermesWpfTerminalUi47.dll"
if (-not (Test-Path $dll)) { throw "Missing $dll" }
Write-Host "OK: $dll" -ForegroundColor Green
if (-not $SkipMt5Deploy) { & (Join-Path $root "Deploy-To-MT5.ps1") }
Write-Host "MT5: F7 EA, reattach, Reset. InpWpfUi47=$dll" -ForegroundColor Yellow
