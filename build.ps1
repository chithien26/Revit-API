<#
.SYNOPSIS
    Builds CTTools in Release and packages it into dist\ as a zip and (if Inno Setup is installed) a Setup.exe.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipInstaller
#>
param(
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

[xml]$props = Get-Content "$root\Directory.Build.props"
$version = $props.Project.PropertyGroup.Version
$revitVersion = $props.Project.PropertyGroup.RevitVersion

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "CTTools-$revitVersion"
$bin = Join-Path $root "src\CTTools\bin\Release"

if (-not (Test-Path "$root\secrets.props") -and -not $env:LicenseConnectionString) {
    throw "No license server configured. Copy secrets.props.example to secrets.props and fill it in."
}

Write-Host "==> Building CTTools $version for Revit $revitVersion" -ForegroundColor Cyan
dotnet build "$root\src\CTTools\CTTools.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

Write-Host "==> Staging files" -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item "$stage\CTTools" -ItemType Directory -Force | Out-Null
Copy-Item "$bin\CTTools.addin" $stage
Get-ChildItem $bin -Exclude "CTTools.addin", "*.pdb" | Copy-Item -Destination "$stage\CTTools" -Recurse

$zip = Join-Path $dist "CTTools-Revit$revitVersion-$version.zip"
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force
Write-Host "    Zip: $zip" -ForegroundColor Green

if ($SkipInstaller) { return }

$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $iscc) {
    Write-Warning "Inno Setup not found - skipped Setup.exe. Install it with: winget install JRSoftware.InnoSetup"
    return
}

Write-Host "==> Building installer" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" "/DRevitVersion=$revitVersion" "/DStageDir=$stage" "$root\installer\CTTools.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "    Installer: $dist\CTTools-Revit$revitVersion-$version-Setup.exe" -ForegroundColor Green
