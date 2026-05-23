$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Build = Join-Path $Root "build_exe.ps1"
$Source = Join-Path $Root "TrayApp.cs"
$Icon = Join-Path $Root "logo.ico"
$Png = Join-Path $Root "logo.png"
$Manifest = Join-Path $Root "CC-Tray.manifest"
$Exe = Join-Path $Root "dist-native\CC-Tray.exe"

function Is-NewerThanExe {
    param([string]$Path)
    return (Test-Path $Path) -and ((Get-Item $Path).LastWriteTime -gt (Get-Item $Exe).LastWriteTime)
}

if (-not (Test-Path $Exe) -or (Is-NewerThanExe $Source) -or (Is-NewerThanExe $Icon) -or (Is-NewerThanExe $Png) -or (Is-NewerThanExe $Manifest)) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Build
    if ($LASTEXITCODE -ne 0) {
        throw "Native tray build failed."
    }
}

Start-Process `
    -FilePath $Exe `
    -WorkingDirectory $Root `
    -WindowStyle Hidden
