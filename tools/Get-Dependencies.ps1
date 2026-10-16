<#
.SYNOPSIS
    Downloads FFmpeg (ffplay.exe + ffmpeg.exe + DLLs) into the Engine folder.
    This is the only required native dependency: video rendering uses ffplay,
    and AAC-ELD mirroring audio falls back to an ffmpeg subprocess decoder.
    libfdk-aac is OPTIONAL (better audio quality) and is NOT downloaded here;
    see README for how to build it if you want it.

.PARAMETER Destination
    Target folder (defaults to the Engine dir next to this script's parent build output).
#>
param(
    [string]$Destination = ""
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Destination)) {
    # default: <repo>\WinAirPlay.Gui\bin\Release\net8.0-windows\Engine
    $Destination = Join-Path $PSScriptRoot "..\WinAirPlay.Gui\bin\Release\net8.0-windows\Engine"
}
$Destination = [System.IO.Path]::GetFullPath($Destination)
if (-not (Test-Path $Destination)) { New-Item -ItemType Directory -Path $Destination | Out-Null }

$ffplay = Join-Path $Destination "ffplay.exe"
if (Test-Path $ffplay) {
    Write-Host "[*] ffplay.exe already present in $Destination - skipping download." -ForegroundColor Green
    exit 0
}

# BtbN FFmpeg-Builds (gpl variant) includes ffplay, ffmpeg and all needed DLLs.
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
$zip = Join-Path $env:TEMP "ffmpeg-win64-gpl.zip"

Write-Host "[*] Downloading FFmpeg from $url ..." -ForegroundColor Cyan
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

$tmp = Join-Path $env:TEMP ("ffmpeg_extract_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Write-Host "[*] Extracting..." -ForegroundColor Cyan
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $bin = Get-ChildItem -Path $tmp -Recurse -Filter "ffplay.exe" | Select-Object -First 1
    if (-not $bin) { throw "ffplay.exe not found in the downloaded archive." }
    $srcBin = $bin.DirectoryName
    Write-Host "[*] Copying FFmpeg binaries into $Destination ..." -ForegroundColor Cyan
    foreach ($f in Get-ChildItem -Path $srcBin) {
        Copy-Item -Path $f.FullName -Destination $Destination -Force
    }
}
finally {
    Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $zip -Force -ErrorAction SilentlyContinue
}

if (Test-Path (Join-Path $Destination "ffplay.exe")) {
    Write-Host "[+] Done. FFmpeg (ffplay + ffmpeg) is ready in $Destination" -ForegroundColor Green
    Write-Host "    Optional: build libfdk-aac-2.dll for higher-quality audio (see README)." -ForegroundColor DarkGray
} else {
    Write-Host "[!] ffplay.exe was not found after extraction - please check the download." -ForegroundColor Red
    exit 1
}
