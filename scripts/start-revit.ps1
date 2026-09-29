<#
.SYNOPSIS
    Builds CTTools (Debug, auto-deployed to Revit's add-in folder), starts Revit and waits until .NET is loaded
    so the debugger can attach. Used by the "Revit 2025" debug configuration in .vscode/launch.json.
#>
param(
    [string]$RevitExe = "E:\Setup\Autodesk\Revit 2025\Revit.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (Get-Process Revit -ErrorAction SilentlyContinue) {
    throw "Revit is already running. Close it first (it locks CTTools.dll), or use 'Attach to Revit'."
}
if (-not (Test-Path $RevitExe)) {
    throw "Revit not found at '$RevitExe'. Update the path in scripts/start-revit.ps1."
}

dotnet build "$root\src\CTTools\CTTools.csproj" -c Debug
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "Starting Revit..."
$revit = Start-Process $RevitExe -PassThru

$deadline = (Get-Date).AddMinutes(3)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($revit.HasExited) { throw "Revit exited during startup." }
    try {
        $revit.Refresh()
        $loaded = $revit.Modules | Where-Object ModuleName -eq "coreclr.dll"
    }
    catch {
        # Module list is not readable yet while the process is initializing
        continue
    }
    if ($loaded) {
        Write-Host "Revit is running (PID $($revit.Id)) - pick Revit.exe in the process list to attach."
        exit 0
    }
}
throw "Timed out waiting for Revit to load .NET."
