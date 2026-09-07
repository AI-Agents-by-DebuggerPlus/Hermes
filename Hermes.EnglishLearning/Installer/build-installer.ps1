#requires -Version 3.0
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$ZipOnly
)

$ErrorActionPreference = "Stop"
$InstallerDir = $PSScriptRoot
$ProjectDir = Split-Path -Parent $InstallerDir
$Proj = Join-Path $ProjectDir "Hermes.EnglishLearning.csproj"
$OutApp = Join-Path $ProjectDir "bin\$Configuration\net48"
$OutputDir = Join-Path $InstallerDir "output"
$StageApp = Join-Path $OutputDir "app-payload"
$Version = "1.1.3"
$excludeDirs = @("logs", "tts-cache")

Write-Host "Project: $Proj"
Write-Host "Publish: $OutApp"

if (-not $SkipBuild) {
    Write-Host "Building $Configuration..."
    & dotnet build $Proj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }
}

$exe = Join-Path $OutApp "Hermes.EnglishLearning.exe"
if (-not (Test-Path $exe)) {
    throw "Missing EXE: $exe - build first."
}

# Stop running instances so files are not locked
Get-Process Hermes.EnglishLearning -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function New-CleanPayload {
    if (Test-Path $StageApp) { Remove-Item $StageApp -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $StageApp | Out-Null
    Copy-Item -Path (Join-Path $OutApp "*") -Destination $StageApp -Recurse -Force
    foreach ($d in $excludeDirs) {
        $p = Join-Path $StageApp $d
        if (Test-Path $p) { Remove-Item $p -Recurse -Force }
    }
    Get-ChildItem $StageApp -Filter "*.pdb" -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
    # Do not ship machine-local settings with API keys
    $settings = Join-Path $StageApp "settings.json"
    if (Test-Path $settings) { Remove-Item $settings -Force }
    # Minimal portable defaults (no secrets)
    $defaults = @{
        TtsProvider = "Sapi"
        VolumePercent = 80
        RecipientName = "EnglishLearning"
        AutoSpeak = $false
        EnglishFontSize = 42
        RussianFontSize = 28
        EnglishColor = "#F8D12F"
        RussianColor = "#D0D4DC"
        WordColumns = 2
        AzureEnglishVoice = "en-US-JennyNeural"
        AzureRussianVoice = "ru-RU-SvetlanaNeural"
    }
    ($defaults | ConvertTo-Json) | Set-Content -Path $settings -Encoding UTF8
    return $StageApp
}

$payload = New-CleanPayload
Write-Host "Clean payload: $payload"

function Find-ISCC {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        (Join-Path $InstallerDir "tools\Inno Setup 6\ISCC.exe"),
        (Join-Path $InstallerDir "tools\Inno\ISCC.exe"),
        (Join-Path $InstallerDir "tools\InnoSetup6\ISCC.exe")
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Install-InnoSetupPortable {
    $tools = Join-Path $InstallerDir "tools"
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    $setup = Join-Path $tools "innosetup-install.exe"
    $url = "https://jrsoftware.org/download.php/is.exe"
    Write-Host "Downloading Inno Setup from $url ..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $setup -UseBasicParsing
    } catch {
        Write-Warning "Download failed: $($_.Exception.Message)"
        return $null
    }
    $target = Join-Path $tools "Inno Setup 6"
    Write-Host "Installing Inno Setup silently to $target ..."
    $p = Start-Process -FilePath $setup -ArgumentList @(
        "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$target"
    ) -Wait -PassThru
    Write-Host "Inno Setup installer exit=$($p.ExitCode)"
    $iscc = Join-Path $target "ISCC.exe"
    if (Test-Path $iscc) { return $iscc }
    return (Find-ISCC)
}

function New-ZipFallback {
    $zipPath = Join-Path $OutputDir "HermesEnglishLearning-$Version-Win7.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    $stage = Join-Path $OutputDir "stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $StageApp "*") -Destination $stage -Recurse -Force

    $cmdSrc = Join-Path $InstallerDir "Install-EnglishLearning.cmd"
    Copy-Item $cmdSrc (Join-Path $stage "Install-EnglishLearning.cmd") -Force

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)
    Remove-Item $stage -Recurse -Force
    Write-Host "Created zip package: $zipPath"
    Write-Host "On target PC: unzip and run Install-EnglishLearning.cmd"
    return $zipPath
}

if ($ZipOnly) {
    New-ZipFallback | Out-Null
    exit 0
}

$iscc = Find-ISCC
if (-not $iscc) {
    Write-Host "ISCC not found - trying to install Inno Setup..."
    $iscc = Install-InnoSetupPortable
}

if ($iscc) {
    Write-Host "Using ISCC: $iscc"
    $iss = Join-Path $InstallerDir "EnglishLearning.iss"
    & $iscc "/DSourceDir=$StageApp" "/DOutDir=$OutputDir" $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
    Get-ChildItem $OutputDir -Filter "HermesEnglishLearning-Setup-*.exe" |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 |
        ForEach-Object { Write-Host "Installer ready: $($_.FullName)" }
} else {
    Write-Warning "Could not obtain Inno Setup. Creating zip + Install-EnglishLearning.cmd fallback (Win7 OK)."
    New-ZipFallback | Out-Null
}
