# Brainarr Plugin Build and Deployment Script
# This is a simple wrapper around the main build.ps1 script

param(
    [string]$DeployPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Brainarr",
    [switch]$Clean,
    [switch]$Test,
    [switch]$Package
)

Write-Host ""
Write-Host "Brainarr Plugin Build & Deploy Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script is now a wrapper around build.ps1" -ForegroundColor Yellow
Write-Host "For more options, use build.ps1 directly." -ForegroundColor Yellow
Write-Host ""

# Build arguments
$buildArgs = @()

if ($Clean) { $buildArgs += "-Clean" }
if ($Test) { $buildArgs += "-Test" }
if ($Package) { $buildArgs += "-Package" }

# Always deploy
$buildArgs += "-Deploy"
$buildArgs += "-DeployPath"
$buildArgs += $DeployPath

Write-Host "Running: .\build.ps1 $($buildArgs -join ' ')" -ForegroundColor Cyan
Write-Host ""

# Execute the main build script
& ".\build.ps1" @buildArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=====================================" -ForegroundColor Green
    Write-Host "Build and Deploy Complete!" -ForegroundColor Green
    Write-Host "=====================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Plugin deployed to: $DeployPath" -ForegroundColor White
    Write-Host "Restart Lidarr to load the updated plugin." -ForegroundColor Yellow
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}