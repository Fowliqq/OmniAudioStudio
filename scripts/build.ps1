<#
.SYNOPSIS
    Builds the OmniAudioStudio solution and optionally publishes a standalone release executable.
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$PublishSingleFile
)

$ErrorActionPreference = "Stop"
$rootDir = (Join-Path $PSScriptRoot "..")
$slnPath = Join-Path $rootDir "OmniAudioStudio.sln"

Write-Host ">>> Restoring and Building OmniAudioStudio ($Configuration)..." -ForegroundColor Cyan
dotnet build $slnPath -c $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[SUCCESS] Solution build completed." -ForegroundColor Green

if ($PublishSingleFile) {
    Write-Host ">>> Publishing Self-Contained Single-File Executable..." -ForegroundColor Cyan
    $projectPath = Join-Path $rootDir "OmniAudioStudio.Desktop\OmniAudioStudio.Desktop.csproj"
    $outDir = Join-Path $rootDir "dist"
    
    dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "[SUCCESS] Executable published to: $outDir\OmniAudioStudio.exe" -ForegroundColor Green
    }
}
