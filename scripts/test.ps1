<#
.SYNOPSIS
    Runs the automated test suite for OmniAudioStudio.
#>

$ErrorActionPreference = "Stop"
$rootDir = (Join-Path $PSScriptRoot "..")
$testProj = Join-Path $rootDir "OmniAudioStudio.Tests\OmniAudioStudio.Tests.csproj"

Write-Host ">>> Executing OmniAudioStudio Verification Tests..." -ForegroundColor Cyan
dotnet run --project $testProj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[SUCCESS] All tests executed successfully." -ForegroundColor Green
