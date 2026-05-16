# Submit Reni vodokanal meter reading (Playwright + saved browser session).
param(
    [switch]$login,
    [switch]$Ack,
    [switch]$Notify,
    [switch]$CheckSession
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

$venv = Join-Path $here ".venv"
if (-not (Test-Path $venv)) {
    python -m venv $venv
    & (Join-Path $venv "Scripts\pip.exe") install -r requirements.txt
    & (Join-Path $venv "Scripts\playwright.exe") install chromium
}

$pyArgs = @()
if ($login) { $pyArgs += "--login" }
if ($Ack) { $pyArgs += "--ack" }
if ($Notify) { $pyArgs += "--notify" }
if ($CheckSession) { $pyArgs += "--check-session" }

& (Join-Path $venv "Scripts\python.exe") (Join-Path $here "submit_reni_water_reading.py") @pyArgs
exit $LASTEXITCODE
