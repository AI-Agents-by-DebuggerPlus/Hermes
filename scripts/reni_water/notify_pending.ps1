# Hourly reminder while water-meter submit awaits your acknowledgment.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

$venvPy = Join-Path $here ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPy)) {
    Write-Host "venv missing; run .\run_submit.ps1 once to install."
    exit 1
}

& $venvPy (Join-Path $here "submit_reni_water_reading.py") --notify 2>&1 | Write-Host
# Exit 1 = pending ack exists → show reminder. Exit 0 = nothing to remind.
if ($LASTEXITCODE -eq 0) {
    exit 0
}

$msg = @(
    "Hermes / Reni vodokanal"
    "Показания переданы. Проверьте скриншот в HermesScreenShots."
    "Подтвердите: .\run_submit.ps1 -Ack"
    "(или напишите Hermes: принял / понял)"
) -join "`n"

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.MessageBox]::Show(
    $msg,
    "Показания воды — нужно подтверждение",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Warning
) | Out-Null

exit 0
