$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-CSharpCompiler {
    $candidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "The .NET Framework C# compiler was not found."
}

$Compiler = Find-CSharpCompiler
$Source = Join-Path $Root "TrayApp.cs"
$Dist = Join-Path $Root "dist-native"
$Exe = Join-Path $Dist "CC-Tray.exe"
$LogoPng = Join-Path $Root "logo.png"
$LogoIco = Join-Path $Root "logo.ico"
$Manifest = Join-Path $Root "CC-Tray.manifest"

if (-not (Test-Path $Source)) {
    throw "Missing native tray source: $Source"
}
if (-not (Test-Path $LogoPng)) {
    throw "Missing tray PNG: $LogoPng"
}
if (-not (Test-Path $LogoIco)) {
    throw "Missing executable icon: $LogoIco"
}
if (-not (Test-Path $Manifest)) {
    throw "Missing application manifest: $Manifest"
}

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
& $Compiler @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/platform:x64",
    "/win32icon:$LogoIco",
    "/win32manifest:$Manifest",
    "/reference:System.dll",
    "/reference:System.Core.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Windows.Forms.dll",
    "/resource:$LogoPng,CCTray.logo.png",
    "/out:$Exe",
    $Source
)
if ($LASTEXITCODE -ne 0) {
    throw "Native tray build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $Exe"
