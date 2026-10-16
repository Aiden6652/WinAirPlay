<#
.SYNOPSIS
    Build WinAirPlay and produce a ready-to-run distributable folder.
    By default publishes BOTH the GUI and the AirPlay engine as
    self-contained win-x64 (no .NET runtime install required).

.OUTPUT
    .\dist\WinAirPlay\   ->  final folder you can zip / install
#>
param(
    [switch]$FrameworkDependent   # use this to avoid bundling the .NET runtime (smaller, needs .NET 8 Desktop Runtime)
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    $sc = if ($FrameworkDependent) { "" } else { "--self-contained -r win-x64" }
    $out = Join-Path $root "dist" "WinAirPlay"

    # 1) Publish the WPF GUI
    Write-Host "[1/4] Publishing GUI..." -ForegroundColor Cyan
    dotnet publish "WinAirPlay.Gui\WinAirPlay.Gui.csproj" -c Release $sc `
        -o (Join-Path $out)

    # 2) Publish the AirPlay engine
    Write-Host "[2/4] Publishing AirPlay engine..." -ForegroundColor Cyan
    $engineOut = Join-Path $root "dist" "AirPlayEngine"
    dotnet publish "AirPlay\AirPlay.csproj" -c Release $sc `
        -o $engineOut

    # 3) Merge engine into the GUI's "Engine" subfolder
    Write-Host "[3/4] Packaging engine into Engine folder..." -ForegroundColor Cyan
    $engineDest = Join-Path $out "Engine"
    if (Test-Path $engineDest) { Remove-Item $engineDest -Recurse -Force }
    New-Item -ItemType Directory -Path $engineDest | Out-Null
    # copy AppHost exe + all engine output (dlls, appsettings_win.json, etc.)
    Copy-Item -Path (Join-Path $engineOut "*") -Destination $engineDest -Recurse -Force

    # 4) Fetch FFmpeg (required for video + audio)
    Write-Host "[4/4] Fetching FFmpeg dependencies..." -ForegroundColor Cyan
    & "$PSScriptRoot\Get-Dependencies.ps1" -Destination $engineDest

    Write-Host "" 
    Write-Host "[+] Build complete. Distributable folder:" -ForegroundColor Green
    Write-Host "    $out" -ForegroundColor Green
    Write-Host "    Run WinAirPlay.exe -> it auto-starts receiving; pick your PC from the iPad's Screen Mirroring." -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
