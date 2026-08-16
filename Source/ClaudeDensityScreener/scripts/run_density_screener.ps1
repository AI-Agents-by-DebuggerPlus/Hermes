#Requires -Version 5.1
<#
.SYNOPSIS
  Starts Hermes Density Screener (Spot or Futures Demo → LocalAppData IPC).
.EXAMPLE
  .\run_density_screener.ps1
  .\run_density_screener.ps1 -Market futures-demo
#>
param(
    [string]$Symbol = "BTCUSDT",
    [ValidateSet("spot", "futures-demo")]
    [string]$Market = "spot",
    [double]$PersistenceSec = 3,
    [double]$SnapshotIntervalSec = 1
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "density_screener\cli.py"))) {
    $Root = $PSScriptRoot
}
Set-Location $Root

$bridge = Join-Path $env:LOCALAPPDATA "HermesDensity\bridge"
New-Item -ItemType Directory -Force -Path $bridge | Out-Null

Write-Host "Density Screener → $bridge\density_snapshot.json"
Write-Host "Symbol=$Symbol Market=$Market  (Ctrl+C to stop)"
$env:PYTHONPATH = $Root
python -m density_screener.cli `
    --symbol $Symbol `
    --market $Market `
    --persistence-sec $PersistenceSec `
    --snapshot-interval-sec $SnapshotIntervalSec
