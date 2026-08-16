#Requires -Version 5.1
<#
.SYNOPSIS
  Automated checks for Trading Analytics ecosystem (Density Screener + IPC + kit files).
.EXAMPLE
  .\qa\run_all_checks.ps1
  .\qa\run_all_checks.ps1 -SkipLiveDensity
#>
param(
    [switch]$SkipLiveDensity,
    [int]$LiveSeconds = 12
)

$ErrorActionPreference = "Continue"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $ProjectRoot "AGENTS.md"))) {
    $ProjectRoot = $PSScriptRoot
}
$ReportPath = Join-Path $PSScriptRoot "last_report.txt"
$lines = New-Object System.Collections.Generic.List[string]
$fail = 0

function Say([string]$msg, [string]$level = "INFO") {
    $stamp = Get-Date -Format "HH:mm:ss"
    $line = "[$stamp][$level] $msg"
    $lines.Add($line)
    if ($level -eq "FAIL") { Write-Host $line -ForegroundColor Red }
    elseif ($level -eq "PASS") { Write-Host $line -ForegroundColor Green }
    elseif ($level -eq "WARN") { Write-Host $line -ForegroundColor Yellow }
    else { Write-Host $line }
}

function Fail([string]$msg) {
    $script:fail++
    Say $msg "FAIL"
}

function Pass([string]$msg) { Say $msg "PASS" }

Say "=== Trading Analytics QA ==="
Say "ProjectRoot=$ProjectRoot"

# Resolve Hermes repo (sibling of HermesProjects)
$HermesRoot = $null
foreach ($cand in @(
        (Join-Path $ProjectRoot "..\..\Hermes"),
        (Join-Path $ProjectRoot "..\Hermes"),
        "D:\Programming\AI_Agents\Hermes"
    )) {
    $full = [IO.Path]::GetFullPath($cand)
    if (Test-Path (Join-Path $full "Source\ClaudeDensityScreener\density_screener\cli.py")) {
        $HermesRoot = $full
        break
    }
}

if (-not $HermesRoot) {
    Fail "Hermes repo not found (expected ..\..\Hermes next to HermesProjects)"
    $lines | Set-Content -Path $ReportPath -Encoding UTF8
    exit 1
}
Pass "HermesRoot=$HermesRoot"

$DensityRoot = Join-Path $HermesRoot "Source\ClaudeDensityScreener"
$EcoDir = Join-Path $ProjectRoot "hermes\ecosystem"

# --- Kit files ---
Say "--- Ecosystem kit files ---"
$requiredEco = @(
    "INDEX.md", "apps.md", "live-data.md",
    "howto-density.md", "howto-chart-screenshot.md", "howto-market-context.md"
)
foreach ($f in $requiredEco) {
    $p = Join-Path $EcoDir $f
    if (Test-Path $p) { Pass "ecosystem/$f" }
    else { Fail "missing hermes/ecosystem/$f" }
}

$agents = Join-Path $ProjectRoot "AGENTS.md"
if ((Test-Path $agents) -and (Select-String -Path $agents -Pattern "Trading Analytics" -Quiet)) {
    Pass "AGENTS.md has Trading Analytics section"
} else {
    Fail "AGENTS.md missing Trading Analytics ecosystem section"
}

$qaReadme = Join-Path $PSScriptRoot "README.md"
$qaManual = Join-Path $PSScriptRoot "MANUAL_CHECKLIST.md"
if (Test-Path $qaReadme) { Pass "qa/README.md" } else { Fail "missing qa/README.md" }
if (Test-Path $qaManual) { Pass "qa/MANUAL_CHECKLIST.md" } else { Fail "missing qa/MANUAL_CHECKLIST.md" }

# --- Density unit tests ---
Say "--- Density Screener pytest ---"
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) {
    Fail "python not on PATH"
} else {
    Push-Location $DensityRoot
    $env:PYTHONPATH = $DensityRoot
    $pytestOut = & python -m pytest tests/ -q 2>&1 | Out-String
    $pytestCode = $LASTEXITCODE
    Pop-Location
    if ($pytestCode -eq 0) {
        Pass "pytest density_screener (exit 0)"
    } else {
        Fail "pytest failed:`n$pytestOut"
    }

    Push-Location $DensityRoot
    $env:PYTHONPATH = $DensityRoot
    $simOut = & python examples\simulate_run.py 2>&1 | Out-String
    $simCode = $LASTEXITCODE
    Pop-Location
    if ($simCode -eq 0 -and $simOut -match "levels=") {
        Pass "simulate_run.py"
    } else {
        Fail "simulate_run.py failed:`n$simOut"
    }
}

# --- Skill on disk (WSL optional) ---
Say "--- Agent skill density-snapshot ---"
$skillRepo = Join-Path $HermesRoot "Docs\TradingAnalytics\skills\density-snapshot\SKILL.md"
if (Test-Path $skillRepo) { Pass "skill source in Hermes Docs" }
else { Fail "missing Docs/TradingAnalytics/skills/density-snapshot/SKILL.md" }

$skillOk = $false
try {
    $chk = & wsl -e bash -c 'test -f $HOME/.hermes/skills/domain/density-snapshot/SKILL.md; echo $?' 2>$null
    if ("$chk".Trim() -eq "0") { $skillOk = $true }
} catch { }
if ($skillOk) { Pass "skill installed in ~/.hermes/skills/domain/density-snapshot" }
else { Say "skill not in WSL ~/.hermes (agent may still use howto-density.md)" "WARN" }

# --- Live density optional ---
$bridge = Join-Path $env:LOCALAPPDATA "HermesDensity\bridge"
$snap = Join-Path $bridge "density_snapshot.json"
$hb = Join-Path $bridge "heartbeat.txt"

if (-not $SkipLiveDensity) {
    Say "--- Live density smoke (${LiveSeconds}s) ---"
    if (-not $py) {
        Fail "skip live: no python"
    } else {
        $env:PYTHONPATH = $DensityRoot
        $errLog = Join-Path $env:TEMP "ta_density_err.txt"
        $outLog = Join-Path $env:TEMP "ta_density_out.txt"
        $proc = Start-Process -FilePath "python" -ArgumentList @(
            "-m", "density_screener.cli",
            "--symbol", "BTCUSDT",
            "--persistence-sec", "2",
            "--snapshot-interval-sec", "1"
        ) -WorkingDirectory $DensityRoot -PassThru -WindowStyle Hidden `
            -RedirectStandardError $errLog -RedirectStandardOutput $outLog
        Start-Sleep -Seconds $LiveSeconds
        if ($proc -and -not $proc.HasExited) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 1

        Push-Location $DensityRoot
        $env:PYTHONPATH = $DensityRoot
        $sum = & python scripts\summarize_density.py 2>&1 | Out-String
        Pop-Location

        if ((Test-Path $snap) -and ($sum -match "levels=")) {
            Pass "live snapshot + summarize"
            Say (($sum.Trim() -split "`r?`n" | Select-Object -First 6) -join " | ")
            if ($sum -notmatch "STATUS: screener OK") {
                Say "heartbeat may be stale right after stop - OK for smoke" "WARN"
            }
        } else {
            Fail "live density smoke failed:`n$sum"
            if (Test-Path $errLog) {
                $errTail = (Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -Last 8) -join " | "
                Say "stderr: $errTail" "WARN"
            }
        }
    }
} else {
    Say "SkipLiveDensity: checking existing IPC only" "WARN"
    if (Test-Path $snap) { Pass "existing density_snapshot.json" }
    else { Say "no density_snapshot.json yet (start screener for D1-D4)" "WARN" }
    if (Test-Path $hb) { Pass "existing heartbeat.txt" }
    else { Say "no heartbeat.txt" "WARN" }
}

# --- Futures bridge presence (informational) ---
Say "--- Futures bridge (informational) ---"
$ftHb = Join-Path $env:LOCALAPPDATA "HermesTrading\bridge\heartbeat.txt"
$ftSnap = Join-Path $env:LOCALAPPDATA "HermesTrading\bridge\snapshot.json"
if (Test-Path $ftHb) { Pass "HermesTrading heartbeat exists" }
else { Say "Futures terminal heartbeat absent (OK if terminal not running)" "WARN" }
if (Test-Path $ftSnap) { Pass "HermesTrading snapshot.json exists" }
else { Say "Futures snapshot absent (OK if terminal not running)" "WARN" }

# --- Manual pointer ---
Say "--- Manual / visual ---"
Say "Open: qa\MANUAL_CHECKLIST.md  (sections D*, F*, H*, P*)"
Pass "manual checklist file present"

# --- Summary ---
Say "=== SUMMARY fails=$fail ==="
if ($fail -eq 0) {
    Pass "ALL AUTOMATED CHECKS PASSED"
    Say "Next: run through MANUAL_CHECKLIST.md (Density D1-D4 at minimum)."
} else {
    Fail "AUTOMATED CHECKS FAILED ($fail)"
}

$lines | Set-Content -Path $ReportPath -Encoding UTF8
Say "Report -> $ReportPath"
exit $(if ($fail -gt 0) { 1 } else { 0 })
