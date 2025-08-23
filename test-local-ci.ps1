# Local CI Testing Script
# This script mimics the GitHub Actions CI environment locally

param(
    [string]$DotNetVersion = "6.0.x",
    [switch]$SkipDownload,
    [switch]$Verbose
)

Write-Host "🧪 Starting Local CI Test (mimicking GitHub Actions)" -ForegroundColor Green
Write-Host "Target .NET Version: $DotNetVersion" -ForegroundColor Cyan

# Step 1: Clean up any previous runs
Write-Host "`n🧹 Cleaning previous artifacts..." -ForegroundColor Yellow
if (Test-Path "lidarr.tar.gz") { Remove-Item "lidarr.tar.gz" -Force }
if (Test-Path "Lidarr") { Remove-Item "Lidarr" -Recurse -Force }
if (Test-Path "TestResults") { Remove-Item "TestResults" -Recurse -Force }

# Step 2: Ensure ext/Lidarr/_output/net6.0 directory exists
Write-Host "`n📁 Setting up Lidarr assemblies directory..." -ForegroundColor Yellow
$lidarrPath = "ext/Lidarr/_output/net6.0"
if (-not (Test-Path $lidarrPath)) {
    New-Item -ItemType Directory -Path $lidarrPath -Force | Out-Null
}

# Step 3: Download Lidarr assemblies (same as CI)
if (-not $SkipDownload) {
    Write-Host "`n⬇️ Downloading Lidarr assemblies..." -ForegroundColor Yellow
    
    try {
        # Get latest release URL (same as CI)
        $apiResponse = Invoke-RestMethod -Uri "https://api.github.com/repos/Lidarr/Lidarr/releases/latest"
        $downloadUrl = $apiResponse.assets | Where-Object { $_.name -like "*linux-core-x64.tar.gz" } | Select-Object -First 1 -ExpandProperty browser_download_url
        
        if ($downloadUrl) {
            Write-Host "Downloading from: $downloadUrl" -ForegroundColor Cyan
            Invoke-WebRequest -Uri $downloadUrl -OutFile "lidarr.tar.gz"
        } else {
            Write-Host "Using fallback URL..." -ForegroundColor Yellow
            Invoke-WebRequest -Uri "https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz" -OutFile "lidarr.tar.gz"
        }
        
        # Extract using tar (requires WSL or Git Bash on Windows)
        Write-Host "Extracting Lidarr archive..." -ForegroundColor Cyan
        & tar -xzf lidarr.tar.gz
        
        # Copy required assemblies
        if (Test-Path "Lidarr") {
            Copy-Item "Lidarr/Lidarr.Core.dll" "$lidarrPath/" -ErrorAction SilentlyContinue
            Copy-Item "Lidarr/Lidarr.Common.dll" "$lidarrPath/" -ErrorAction SilentlyContinue
            Copy-Item "Lidarr/Lidarr.Http.dll" "$lidarrPath/" -ErrorAction SilentlyContinue
            Copy-Item "Lidarr/Lidarr.Api.V1.dll" "$lidarrPath/" -ErrorAction SilentlyContinue
            
            Write-Host "✅ Downloaded assemblies:" -ForegroundColor Green
            Get-ChildItem $lidarrPath -Name
        } else {
            throw "Failed to extract Lidarr archive"
        }
        
    } catch {
        Write-Error "❌ Failed to download Lidarr assemblies: $($_.Exception.Message)"
        exit 1
    }
} else {
    Write-Host "⏭️ Skipping download (using existing assemblies)" -ForegroundColor Yellow
}

# Step 4: Set environment variable (same as CI)
$env:LIDARR_PATH = Resolve-Path $lidarrPath
Write-Host "`n🔧 Set LIDARR_PATH=$($env:LIDARR_PATH)" -ForegroundColor Cyan

# Step 5: Restore dependencies
Write-Host "`n📦 Restoring dependencies..." -ForegroundColor Yellow
dotnet restore Brainarr.sln
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Failed to restore dependencies"
    exit 1
}

# Step 6: Build (same as CI)
Write-Host "`n🔨 Building solution..." -ForegroundColor Yellow
$buildArgs = @(
    "build", "Brainarr.sln",
    "--no-restore",
    "--configuration", "Release",
    "-p:LidarrPath=$($env:LIDARR_PATH)"
)

if ($Verbose) {
    $buildArgs += "--verbosity", "detailed"
}

dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Build failed"
    exit 1
}

# Step 7: Run tests (same as CI)
Write-Host "`n🧪 Running tests..." -ForegroundColor Yellow
if (-not (Test-Path "TestResults")) {
    New-Item -ItemType Directory -Path "TestResults" | Out-Null
}

$testArgs = @(
    "test", "Brainarr.sln",
    "--no-build",
    "--configuration", "Release",
    "--verbosity", "normal",
    "--collect:XPlat Code Coverage",
    "--logger", "trx;LogFileName=test-results.trx",
    "--results-directory", "TestResults/",
    "--blame-hang-timeout", "5m"
)

dotnet @testArgs
$testExitCode = $LASTEXITCODE

# Step 8: Show results
Write-Host "`n📊 Test Results:" -ForegroundColor Yellow
if (Test-Path "TestResults") {
    Get-ChildItem "TestResults" -Recurse -Name
}

if ($testExitCode -eq 0) {
    Write-Host "✅ All tests passed!" -ForegroundColor Green
} else {
    Write-Host "⚠️ Some tests failed (exit code: $testExitCode)" -ForegroundColor Red
}

Write-Host "`n🎯 Local CI test complete!" -ForegroundColor Green
Write-Host "This environment now matches your GitHub Actions CI setup." -ForegroundColor Cyan

exit $testExitCode