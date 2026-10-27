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
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("OmniAudioStudio-FFmpeg-" + [Guid]::NewGuid().ToString("N"))
$tempZip = Join-Path $tempRoot "ffmpeg-essentials.zip"
$tempExtract = Join-Path $tempRoot "extracted"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    
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
    if (Test-Path $tempRoot) { Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
