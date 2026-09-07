#requires -Version 3.0
<#
.SYNOPSIS
  Builds portable VAIO_OFLINE_PACK on a working PC (USB) for offline transfer
  to Sony VAIO VPCCB15FD (custom Windows7_x8, no internet on target).

.DESCRIPTION
  Run on a HEALTHY machine with internet. Creates folder structure, downloads
  packages, and generates install_all.bat for the target laptop (cmd only).

.PARAMETER DestinationRoot
  Parent folder where VAIO_OFLINE_PACK will be created (example: E:\ ).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Build-VaioOfflinePack.ps1 -DestinationRoot E:\
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$DestinationRoot = ""
)

$ErrorActionPreference = "Continue"
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
    Write-Warning ("Could not set TLS 1.2: " + $_.Exception.Message)
}

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Read-Host "Enter USB/parent path (example: E:\ )"
}
$DestinationRoot = $DestinationRoot.Trim().TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath $DestinationRoot)) {
    throw ("DestinationRoot does not exist: " + $DestinationRoot)
}

$PackName = "VAIO_OFLINE_PACK"
$Root = Join-Path $DestinationRoot $PackName

$DirNet = Join-Path $Root "1_NET_Framework_48"
$DirWifi = Join-Path $Root "2_Drivers_WiFi"
$DirLan = Join-Path $Root "3_Drivers_LAN"
$DirFix = Join-Path $Root "4_Fixes_Windows7"

foreach ($d in @($Root, $DirNet, $DirWifi, $DirLan, $DirFix)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

Write-Host ""
Write-Host "=== VAIO offline pack builder ===" -ForegroundColor Cyan
Write-Host ("Target: " + $Root)
Write-Host ""

# ASCII-only strings (safe for Windows PowerShell default encoding)
$Downloads = @(
    @{
        Name = "ndp48-x86-x64-allos-enu.exe"
        Dir  = $DirNet
        Url  = "https://go.microsoft.com/fwlink/?linkid=2088631"
        AltUrls = @(
            "https://download.visualstudio.microsoft.com/download/pr/2d6bb6b2-226a-4baa-bdec-798822606ff1/8494001c276a4b96804cde7829c04d7f/ndp48-x86-x64-allos-enu.exe"
        )
        Note = ".NET Framework 4.8 offline installer (x86/x64). Correct for Windows 7 SP1."
        ManualUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    }
    @{
        Name = "ndp48-devpack-enu.exe"
        Dir  = $DirNet
        Url  = "https://go.microsoft.com/fwlink/?linkid=2088517"
        Note = ".NET Framework 4.8 Developer Pack (optional, for building)."
        ManualUrl = "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    }
    @{
        Name = "Intel_Wireless_Win7_64.exe"
        Dir  = $DirWifi
        Url  = "https://downloadmirror.intel.com/18725/eng/Wireless_15.1.1_PROSet64_Win7.exe"
        AltUrls = @(
            "https://downloadmirror.intel.com/25016/eng/Wireless_19.40.0_PROSet64_Win7.exe"
        )
        Note = "Intel Wi-Fi Win7 x64 (VPCCB15FD often Centrino N1000). CDN may return 403."
        ManualUrl = "https://www.sony.com/electronics/support/laptop-pc-vpc-series/vpccb15fd"
    }
    @{
        Name = "Atheros_Wireless_Win7_64.exe"
        Dir  = $DirWifi
        Url  = ""
        Note = "Atheros Wi-Fi Win7 x64 (some VAIO CB have AR9285). No stable public EXE CDN."
        ManualUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=Atheros+Wireless+Windows+7+x64"
        SkipPlaceholder = $true
    }
    @{
        Name = "Broadcom_Wireless_Win7_64.exe"
        Dir  = $DirWifi
        Url  = ""
        Note = "Broadcom Wi-Fi Win7 x64 (fallback for custom images)."
        ManualUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=Broadcom+Wireless+Windows+7+x64"
        SkipPlaceholder = $true
    }
    @{
        Name = "Realtek_Ethernet_Win7_64.exe"
        Dir  = $DirLan
        Url  = ""
        Note = "Realtek LAN Win7 x64. VPCCB15FD LAN is often Atheros AR8151."
        ManualUrl = "https://www.realtek.com/Download/List?cate_id=584"
        SkipPlaceholder = $true
    }
    @{
        Name = "KB4490628_x64.msu"
        Dir  = $DirFix
        Url  = "http://download.windowsupdate.com/c/msdownload/update/software/secu/2019/03/windows6.1-kb4490628-x64_d3de52d6987f7c8bdc2c015dca69eac96047c76e.msu"
        AltUrls = @(
            "https://archive.org/download/windows6.1-kb4490628-x64_d3de52d6987f7c8bdc2c015dca69eac96047c76e.msu/windows6.1-kb4490628-x64_d3de52d6987f7c8bdc2c015dca69eac96047c76e.msu"
        )
        Note = "Servicing Stack Update (install BEFORE SHA-2)"
        ManualUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=KB4490628"
    }
    @{
        Name = "KB4474419_x64.msu"
        Dir  = $DirFix
        Url  = "http://download.windowsupdate.com/c/msdownload/update/software/secu/2019/09/windows6.1-kb4474419-v3-x64_b5614c6cea5cb4e198717789633dca16308ef79c.msu"
        AltUrls = @(
            "https://archive.org/download/windows6.1-kb4474419-v3-x64_b5614c6cea5cb4e198717789633dca16308ef79c.msu/windows6.1-kb4474419-v3-x64_b5614c6cea5cb4e198717789633dca16308ef79c.msu"
        )
        Note = "SHA-2 code signing support for Win7"
        ManualUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=KB4474419"
    }
)

function Get-UrlList {
    param($Item)
    $list = New-Object System.Collections.Generic.List[string]
    if ($Item.Url -and $Item.Url.Trim().Length -gt 0) {
        [void]$list.Add([string]$Item.Url)
    }
    if ($Item.AltUrls) {
        foreach ($a in $Item.AltUrls) {
            if ($a -and $a.Trim().Length -gt 0) {
                [void]$list.Add([string]$a)
            }
        }
    }
    return $list
}

function Save-FileFromUrl {
    param(
        [string]$Url,
        [string]$OutFile
    )

    $headers = @{
        "User-Agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    }

    $bitsOk = $false
    try {
        if (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue) {
            Start-BitsTransfer -Source $Url -Destination $OutFile -ErrorAction Stop
            $bitsOk = $true
        }
    } catch {
        $bitsOk = $false
        if (Test-Path -LiteralPath $OutFile) {
            Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $bitsOk) {
        Invoke-WebRequest -Uri $Url -OutFile $OutFile -UseBasicParsing -Headers $headers -TimeoutSec 600
    }

    if (-not (Test-Path -LiteralPath $OutFile)) {
        throw "File missing after download"
    }
    $len = (Get-Item -LiteralPath $OutFile).Length
    if ($len -lt 1024) {
        throw ("Downloaded file too small (" + $len + " bytes) - likely an HTML error page")
    }
}

function Write-DownloadError {
    param(
        [string]$FileName,
        [string]$UrlHint
    )
    # Required Russian message (UTF-8 bytes via Base64 — encoding-safe in ASCII .ps1)
    $prefix = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
        "0J7QqNCY0JHQmtCQINCh0JrQkNCn0JjQktCQ0J3QmNCvOiA="))
    $mid = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(
        "LiDQodC60LDRh9Cw0LnRgtC1INCy0YDRg9GH0L3Rg9GOINC/0L4g0YHRgdGL0LvQutC1OiA="))
    Write-Host ($prefix + $FileName + $mid + $UrlHint) -ForegroundColor Red
}

$okCount = 0
$failCount = 0
$failList = New-Object System.Collections.Generic.List[string]

foreach ($item in $Downloads) {
    $out = Join-Path $item.Dir $item.Name
    Write-Host (">>> " + $item.Name) -ForegroundColor Yellow
    if ($item.Note) {
        Write-Host ("    " + $item.Note) -ForegroundColor DarkGray
    }

    $hint = $item.Url
    if ($item.ManualUrl) { $hint = $item.ManualUrl }
    if (-not $hint) { $hint = "(no URL)" }

    if ($item.SkipPlaceholder) {
        Write-DownloadError -FileName $item.Name -UrlHint $hint
        $failCount++
        [void]$failList.Add($item.Name)
        $marker = Join-Path $item.Dir ("_MANUAL_" + $item.Name + ".txt")
        @(
            ("Place file here as: " + $item.Name),
            ("Download from: " + $hint),
            $(if ($item.Note) { $item.Note } else { "" })
        ) | Set-Content -Path $marker -Encoding ASCII
        continue
    }

    if ((Test-Path -LiteralPath $out) -and ((Get-Item -LiteralPath $out).Length -gt 1024)) {
        Write-Host ("    SKIP (already exists): " + $out) -ForegroundColor DarkGreen
        $okCount++
        continue
    }

    $urls = Get-UrlList $item
    $done = $false
    foreach ($u in $urls) {
        try {
            Write-Host ("    GET " + $u)
            Save-FileFromUrl -Url $u -OutFile $out
            $size = (Get-Item -LiteralPath $out).Length
            Write-Host ("    OK  " + $out + " (" + $size + " bytes)") -ForegroundColor Green
            $done = $true
            $okCount++
            break
        } catch {
            Write-Host ("    try failed: " + $_.Exception.Message) -ForegroundColor DarkYellow
            if (Test-Path -LiteralPath $out) {
                Remove-Item -LiteralPath $out -Force -ErrorAction SilentlyContinue
            }
        }
    }

    if (-not $done) {
        Write-DownloadError -FileName $item.Name -UrlHint $hint
        $failCount++
        [void]$failList.Add($item.Name)
        $marker = Join-Path $item.Dir ("_MANUAL_" + $item.Name + ".txt")
        @(
            ("Place file here as: " + $item.Name),
            ("Download from: " + $hint)
        ) | Set-Content -Path $marker -Encoding ASCII
    }
}

$failLines = ($failList | ForEach-Object { "  - " + $_ }) -join "`r`n"
if ([string]::IsNullOrWhiteSpace($failLines)) {
    $failLines = "  (none)"
}

$readme = Join-Path $Root "README.txt"
$readmeBody = @"
VAIO_OFLINE_PACK - offline pack for Sony VAIO VPCCB15FD / Windows7_x8
Built: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Host: $env:COMPUTERNAME

CONTENTS
  1_NET_Framework_48\  .NET Framework 4.8 offline + Dev Pack
  2_Drivers_WiFi\      Intel / Atheros / Broadcom Win7 x64
  3_Drivers_LAN\       Realtek Ethernet Win7 x64 (optional)
  4_Fixes_Windows7\    KB4490628 (SSU) then KB4474419 (SHA-2)

IMPORTANT
  - .NET Framework 4.8 is correct for Windows 7 SP1.
  - On target PC run install_all.bat as Administrator (cmd), not PowerShell.
  - Order: KB4490628 -> reboot -> KB4474419 -> reboot -> drivers -> .NET 4.8.
  - VPCCB15FD Wi-Fi is often Intel Centrino Wireless-N 1000 (or Atheros).
  - VPCCB15FD LAN is often Atheros AR8151 (not Realtek).

FAILED AUTO-DOWNLOADS
$failLines

Links
  Catalog: https://www.catalog.update.microsoft.com/
  Sony VPCCB15FD: https://www.sony.com/electronics/support/laptop-pc-vpc-series/vpccb15fd
  .NET 4.8: https://dotnet.microsoft.com/download/dotnet-framework/net48
"@
[System.IO.File]::WriteAllText($readme, $readmeBody, [System.Text.Encoding]::UTF8)

$bat = Join-Path $Root "install_all.bat"
$batBody = @"
@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================
echo  VAIO_OFLINE_PACK installer (cmd / start /wait)
echo  Target: Sony VAIO / Windows 7 x64 custom
echo  Run as Administrator
echo ============================================
echo.
net session >nul 2>&1
if errorlevel 1 (
  echo ERROR: Need Administrator rights.
  pause
  exit /b 1
)
echo.
echo [1/4] Windows 7 Servicing Stack KB4490628 ...
if exist "4_Fixes_Windows7\KB4490628_x64.msu" (
  start /wait wusa.exe "4_Fixes_Windows7\KB4490628_x64.msu" /quiet /norestart
  echo   exit=%ERRORLEVEL%
) else (
  echo   SKIP: KB4490628_x64.msu missing
)
echo.
echo [2/4] Windows 7 SHA-2 KB4474419 ...
if exist "4_Fixes_Windows7\KB4474419_x64.msu" (
  start /wait wusa.exe "4_Fixes_Windows7\KB4474419_x64.msu" /quiet /norestart
  echo   exit=%ERRORLEVEL%
) else (
  echo   SKIP: KB4474419_x64.msu missing
)
echo.
echo [3/4] Network drivers (interactive - match your hardware)...
if exist "3_Drivers_LAN\Realtek_Ethernet_Win7_64.exe" (
  echo   LAN Realtek...
  start /wait "" "3_Drivers_LAN\Realtek_Ethernet_Win7_64.exe"
)
if exist "2_Drivers_WiFi\Intel_Wireless_Win7_64.exe" (
  echo   WiFi Intel...
  start /wait "" "2_Drivers_WiFi\Intel_Wireless_Win7_64.exe"
)
if exist "2_Drivers_WiFi\Atheros_Wireless_Win7_64.exe" (
  echo   WiFi Atheros...
  start /wait "" "2_Drivers_WiFi\Atheros_Wireless_Win7_64.exe"
)
if exist "2_Drivers_WiFi\Broadcom_Wireless_Win7_64.exe" (
  echo   WiFi Broadcom...
  start /wait "" "2_Drivers_WiFi\Broadcom_Wireless_Win7_64.exe"
)
echo.
echo [4/4] .NET Framework 4.8 offline installer
if exist "1_NET_Framework_48\ndp48-x86-x64-allos-enu.exe" (
  echo   Installing .NET Framework 4.8 ...
  start /wait "" "1_NET_Framework_48\ndp48-x86-x64-allos-enu.exe" /passive /norestart
  echo   exit=%ERRORLEVEL%
) else (
  echo   SKIP missing ndp48-x86-x64-allos-enu.exe
)
if exist "1_NET_Framework_48\ndp48-devpack-enu.exe" (
  echo   Installing .NET 4.8 Developer Pack (optional)...
  start /wait "" "1_NET_Framework_48\ndp48-devpack-enu.exe" /passive /norestart
  echo   exit=%ERRORLEVEL%
)
echo.
echo Done. Reboot recommended.
echo See README.txt for notes.
pause
"@
[System.IO.File]::WriteAllText($bat, $batBody, [System.Text.Encoding]::ASCII)

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host ("OK:   " + $okCount)
Write-Host ("FAIL: " + $failCount)
if ($failCount -gt 0) {
    Write-Host "Failed:" -ForegroundColor Red
    $failList | ForEach-Object { Write-Host ("  - " + $_) }
}
Write-Host ""
Write-Host ("Pack ready: " + $Root)
Write-Host "On VAIO: run install_all.bat as Administrator."
Write-Host ""
