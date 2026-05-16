# Register Windows Task Scheduler (run PowerShell as Administrator once).
$ErrorActionPreference = "Stop"
$here = (Resolve-Path $PSScriptRoot).Path
$submitPs1 = Join-Path $here "run_submit.ps1"
$notifyPs1 = Join-Path $here "notify_pending.ps1"

$monthlyName = "Hermes_ReniWater_MonthlySubmit"
$hourlyName = "Hermes_ReniWater_HourlyNotify"

schtasks /Delete /TN $monthlyName /F 2>$null | Out-Null
schtasks /Delete /TN $hourlyName /F 2>$null | Out-Null

schtasks /Create /TN $monthlyName `
    /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$submitPs1`"" `
    /SC MONTHLY /D 1 /ST 09:00 /F /RL LIMITED

schtasks /Create /TN $hourlyName `
    /TR "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$notifyPs1`"" `
    /SC HOURLY /MO 1 /ST 00:10 /F /RL LIMITED

Write-Host "Created scheduled tasks:"
Write-Host "  $monthlyName — day 1 each month, 09:00"
Write-Host "  $hourlyName — every hour (popup only while pending_ack.json exists)"
Write-Host ""
Write-Host "After you confirm: .\run_submit.ps1 -Ack"
