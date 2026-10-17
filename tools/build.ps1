<#
.SYNOPSIS
    Build WinAirPlay as a SINGLE-FILE Windows EXE.

    The AirPlay receiver engine and FFmpeg are bundled into a zip, embedded as a
    resource inside WinAirPlay.exe, and extracted to a temp folder on first launch.
    The delivered package is just one EXE (plus the unavoidable WPF native DLLs).

.OUTPUT
    <repo>\dist\WinAirPlay\   ->  WinAirPlay.exe (+ a few WPF native DLLs)
#>
param(
    [switch]$FrameworkDependent   # skip bundling the .NET runtime (smaller, needs .NET 8 Desktop Runtime)
)

$ErrorActionPreference = 'Stop'

# This script lives in <repo>\tools, so the repo root is its parent.
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # NOTE: must be an ARRAY so PowerShell passes each flag as a separate argument.
    # A single string like "--self-contained -r win-x64" is handed to dotnet as ONE
    # token and MSBuild rejects it (MSB1001: Unknown switch).
    $scArgs = if ($FrameworkDependent) { @() } else { @('--self-contained', '-r', 'win-x64') }
    $out    = Join-Path $root "dist" "WinAirPlay"
    $guiProj = Join-Path $root "WinAirPlay.Gui" "WinAirPlay.Gui.csproj"
    $engProj = Join-Path $root "AirPlay" "AirPlay.csproj"
    $resDir  = Join-Path $root "WinAirPlay.Gui" "Resources"
    $engineZip = Join-Path $resDir "engine.zip"

    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Path $out | Out-Null

    # 1) Publish the AirPlay engine (self-contained worker process)
    Write-Host "[1/4] Publishing AirPlay engine..." -ForegroundColor Cyan
    $engineOut = Join-Path $root "dist" "EngineRaw"
    if (Test-Path $engineOut) { Remove-Item $engineOut -Recurse -Force }
    dotnet publish $engProj -c Release @scArgs -o $engineOut

    # 2) Fetch FFmpeg into the engine folder (so FindFfplay() sees ffplay.exe next to AirPlay.exe)
    Write-Host "[2/4] Fetching FFmpeg..." -ForegroundColor Cyan
    & "$PSScriptRoot\Get-Dependencies.ps1" -Destination $engineOut

    # 3) Zip engine + FFmpeg into the GUI's Resources folder (embedded by the single-file publish)
    Write-Host "[3/4] Packing engine bundle (engine.zip)..." -ForegroundColor Cyan
    if (-not (Test-Path $resDir)) { New-Item -ItemType Directory -Path $resDir | Out-Null }
    if (Test-Path $engineZip) { Remove-Item $engineZip -Force }
    Compress-Archive -Path (Join-Path $engineOut "*") -DestinationPath $engineZip -Force

    # 4) Publish the WPF GUI as a single self-contained file (embeds .NET runtime + engine.zip)
    Write-Host "[4/4] Publishing single-file GUI..." -ForegroundColor Cyan
    $pubArgs = if ($FrameworkDependent) { @() } else { @('--self-contained', '-r', 'win-x64') }
    $pubArgs += @('-p:PublishSingleFile=true', '-p:IncludeAllContentInSingleFile=true')
    dotnet publish $guiProj -c Release @pubArgs -o $out

    # Clean the intermediate engine folder (it lives inside engine.zip now)
    if (Test-Path $engineOut) { Remove-Item $engineOut -Recurse -Force }

    Write-Host ""
    Write-Host "[+] Build complete. Single-file app:" -ForegroundColor Green
    Write-Host "    $out\WinAirPlay.exe" -ForegroundColor Green
    Write-Host "    On first launch it extracts the engine + FFmpeg to a temp folder automatically." -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
