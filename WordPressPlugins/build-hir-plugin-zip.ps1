# WordPress expects: hermes-image-receiver/hermes-image-receiver.php (forward slashes in ZIP).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$pluginSlug = 'hermes-image-receiver'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $root "_hir_extract\$pluginSlug"
$zipPath = Join-Path $root "$pluginSlug.zip"

if (-not (Test-Path (Join-Path $sourceDir "$pluginSlug.php"))) {
    throw "Main plugin file missing: $sourceDir\$pluginSlug.php"
}

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem -Path $sourceDir -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($sourceDir.Length + 1).Replace('\', '/')
        $entryName = "$pluginSlug/$relative"
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entryName)
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Created: $zipPath"
[System.IO.Compression.ZipFile]::OpenRead($zipPath).Entries | ForEach-Object { $_.FullName }
