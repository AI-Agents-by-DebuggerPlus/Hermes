#Requires -Version 5.1
param(
    [switch]$Live,
    [double]$QuantityUsdt = 25,
    [string]$Symbol = ""
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root
$env:PYTHONPATH = $Root
$argsList = @("-m", "density_screener.bounce_strategy", "--quantity-usdt", "$QuantityUsdt")
if ($Symbol) { $argsList += @("--symbol", $Symbol) }
if ($Live) { $argsList += "--live" }
Write-Host "Bounce strategy (default dry-run). Futures terminal required for -Live."
python @argsList
