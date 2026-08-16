#Requires -Version 5.1
<#
.SYNOPSIS
  Kill stale Hermes.Wpf instances, rebuild from source, launch the fresh build.
.EXAMPLE
  .\scripts\run_hermes_wpf.ps1
  .\scripts\run_hermes_wpf.ps1 -Configuration Release
  .\scripts\run_hermes_wpf.ps1 -SkipBuild
  .\scripts\run_hermes_wpf.ps1 -GitPull
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$GitPull,
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

function Say([string]$msg, [string]$color = "Gray") {
    Write-Host $msg -ForegroundColor $color
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $RepoRoot "Hermes.Wpf\Hermes.Wpf.csproj"
$Exe = Join-Path $RepoRoot "Hermes.Wpf\bin\$Configuration\net8.0-windows\Hermes.Wpf.exe"

if (-not (Test-Path $Csproj)) {
    throw "Hermes.Wpf.csproj not found: $Csproj"
}

Say "=== Hermes.Wpf launcher ===" "Cyan"
Say "Repo: $RepoRoot"
Say "Config: $Configuration"

# --- Close all Hermes.Wpf instances (any path) ---
$procs = @(Get-Process -Name "Hermes.Wpf" -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    Say "No running Hermes.Wpf instances." "DarkGray"
} else {
    Say "Closing $($procs.Count) Hermes.Wpf instance(s)..." "Yellow"
    foreach ($p in $procs) {
        try {
            $path = $null
            try { $path = $p.Path } catch { }
            Say "  stop PID=$($p.Id) $path" "Yellow"
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
        } catch {
            Say "  warn: could not stop PID=$($p.Id): $_" "Yellow"
        }
    }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        $left = @(Get-Process -Name "Hermes.Wpf" -ErrorAction SilentlyContinue)
        if ($left.Count -eq 0) { break }
        Start-Sleep -Milliseconds 300
    }
    $left = @(Get-Process -Name "Hermes.Wpf" -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        throw "Hermes.Wpf still running after kill (PIDs: $($left.Id -join ', ')). Close manually and retry."
    }
    Say "All Hermes.Wpf instances closed." "Green"
    Start-Sleep -Milliseconds 500
}

# --- Optional git pull ---
if ($GitPull) {
    Say "git pull..." "Cyan"
    Push-Location $RepoRoot
    try {
        & git pull --ff-only
        if ($LASTEXITCODE -ne 0) {
            throw "git pull failed (exit $LASTEXITCODE)"
        }
        Say "git pull OK" "Green"
    } finally {
        Pop-Location
    }
}

# --- Build ---
if (-not $SkipBuild) {
    Say "dotnet build Hermes.Wpf ($Configuration)..." "Cyan"
    & dotnet build $Csproj -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed (exit $LASTEXITCODE)"
    }
    Say "Build OK" "Green"
} else {
    Say "SkipBuild: using existing binary" "Yellow"
}

if (-not (Test-Path $Exe)) {
    throw "Executable not found: $Exe"
}

$info = Get-Item $Exe
Say "Exe: $Exe"
Say "Built: $($info.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"

if ($NoLaunch) {
    Say "NoLaunch: done." "Green"
    exit 0
}

Say "Starting Hermes.Wpf..." "Cyan"
Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
Start-Sleep -Seconds 1
$running = @(Get-Process -Name "Hermes.Wpf" -ErrorAction SilentlyContinue)
if ($running.Count -eq 0) {
    throw "Hermes.Wpf did not start."
}
Say "Running PID(s): $($running.Id -join ', ')" "Green"
Say "=== Done ===" "Cyan"
