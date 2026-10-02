<#
.SYNOPSIS
    Downloads and extracts official FFmpeg binaries for OmniAudioStudio.
.DESCRIPTION
    Fetches the latest official Windows builds of ffmpeg.exe and ffprobe.exe
    from Gyan.dev and places them into the bin/ directory.
#>

[CmdletBinding()]
param(
    [string]$TargetDir = (Join-Path $PSScriptRoot "..\bin")
)

$ErrorActionPreference = "Stop"

$resolvedDir = [System.IO.Path]::GetFullPath($TargetDir)
if (!(Test-Path $resolvedDir)) {
    New-Item -ItemType Directory -Path $resolvedDir -Force | Out-Null
}

$ffmpegExe = Join-Path $resolvedDir "ffmpeg.exe"
$ffprobeExe = Join-Path $resolvedDir "ffprobe.exe"

if ((Test-Path $ffmpegExe) -and (Test-Path $ffprobeExe)) {
    Write-Host "[OK] FFmpeg and FFprobe are already installed in: $resolvedDir" -ForegroundColor Green
    exit 0
}

Write-Host ">>> Downloading FFmpeg Essentials from Gyan.dev..." -ForegroundColor Cyan
$downloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "ffmpeg-essentials.zip"
$tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "ffmpeg-extract-$(Get-Random)"

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
    Write-Host ">>> Extracting ffmpeg.exe and ffprobe.exe..." -ForegroundColor Cyan
    
    Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
    
    $foundFfmpeg = Get-ChildItem -Path $tempExtract -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    $foundFfprobe = Get-ChildItem -Path $tempExtract -Filter "ffprobe.exe" -Recurse | Select-Object -First 1
    
    if (!$foundFfmpeg -or !$foundFfprobe) {
        throw "Could not locate ffmpeg.exe or ffprobe.exe in extracted archive."
    }
    
    Copy-Item $foundFfmpeg.FullName -Destination $ffmpegExe -Force
    Copy-Item $foundFfprobe.FullName -Destination $ffprobeExe -Force
    
    Unblock-File -Path $ffmpegExe -ErrorAction SilentlyContinue
    Unblock-File -Path $ffprobeExe -ErrorAction SilentlyContinue
    
    Write-Host "[SUCCESS] FFmpeg successfully installed into: $resolvedDir" -ForegroundColor Green
}
finally {
    if (Test-Path $tempZip) { Remove-Item $tempZip -Force -ErrorAction SilentlyContinue }
    if (Test-Path $tempExtract) { Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue }
}
