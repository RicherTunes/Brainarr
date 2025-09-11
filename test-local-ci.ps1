# Local CI Testing Script
# This script mimics the GitHub Actions CI environment locally

param(
    [string]$DotNetVersion = "6.0.x",
    [switch]$SkipDownload,
    [switch]$Verbose,
    [switch]$ExcludeHeavy,
    [switch]$GenerateCoverageReport,
    [switch]$InstallReportGenerator
)

Write-Host "🧪 Starting Local CI Test (mimicking GitHub Actions)" -ForegroundColor Green
Write-Host "Target .NET Version: $DotNetVersion" -ForegroundColor Cyan

# Step 1: Clean up any previous runs
Write-Host "`n🧹 Cleaning previous artifacts..." -ForegroundColor Yellow
if (Test-Path "lidarr.tar.gz") { Remove-Item "lidarr.tar.gz" -Force }
if (Test-Path "Lidarr") { Remove-Item "Lidarr" -Recurse -Force }
if (Test-Path "TestResults") { Remove-Item "TestResults" -Recurse -Force }
try { Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force } catch {}

# Step 2: Ensure ext/Lidarr/_output/net6.0 directory exists
Write-Host "`n📁 Setting up Lidarr assemblies directory..." -ForegroundColor Yellow
$lidarrPath = "ext/Lidarr/_output/net6.0"
if (-not (Test-Path $lidarrPath)) {
    New-Item -ItemType Directory -Path $lidarrPath -Force | Out-Null
}

# Step 3: Obtain Lidarr assemblies (Docker-first, fallback to tar.gz)
if (-not $SkipDownload) {
    Write-Host "`n?? Obtaining Lidarr assemblies..." -ForegroundColor Yellow
    $dockerTag = $env:LIDARR_DOCKER_VERSION
    if ([string]::IsNullOrWhiteSpace($dockerTag)) { $dockerTag = 'pr-plugins-2.13.3.4692' }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($docker) {
        Write-Host "Using Docker image: ghcr.io/hotio/lidarr:$dockerTag" -ForegroundColor Cyan
        & docker pull "ghcr.io/hotio/lidarr:$dockerTag" | Out-Null
        $cid = (& docker create "ghcr.io/hotio/lidarr:$dockerTag").Trim()
        # Copy the entire /app/bin directory to capture all runtime dependencies (e.g., Equ.dll)
        & docker cp "$cid:/app/bin/." "$lidarrPath/" | Out-Null
        & docker rm -f $cid | Out-Null
        Write-Host "Assemblies ready (Docker):" -ForegroundColor Green
        Get-ChildItem $lidarrPath -Name | Sort-Object
    } else {
        Write-Host "Docker not found; falling back to release tarball" -ForegroundColor Yellow
        try {
            $apiResponse = Invoke-RestMethod -Uri "https://api.github.com/repos/Lidarr/Lidarr/releases/latest"
            $downloadUrl = $apiResponse.assets | Where-Object { $_.name -like "*linux-core-x64.tar.gz" } | Select-Object -First 1 -ExpandProperty browser_download_url
            if (-not $downloadUrl) {
                $downloadUrl = "https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz"
            }
            Write-Host "Downloading from: $downloadUrl" -ForegroundColor Cyan
            Invoke-WebRequest -Uri $downloadUrl -OutFile "lidarr.tar.gz"
            Write-Host "Extracting Lidarr archive..." -ForegroundColor Cyan
            & tar -xzf lidarr.tar.gz
            if (Test-Path "Lidarr") {
                foreach ($f in @('Lidarr.Core.dll','Lidarr.Common.dll','Lidarr.Http.dll','Lidarr.Api.V1.dll')) {
                    Copy-Item "Lidarr/$f" "$lidarrPath/" -ErrorAction SilentlyContinue
                }
                Write-Host "Assemblies ready (tar.gz):" -ForegroundColor Green
                Get-ChildItem $lidarrPath -Name | Sort-Object
            } else { throw "Failed to extract Lidarr archive" }
        } catch {
            Write-Error "Failed to fetch Lidarr assemblies: $($_.Exception.Message)"; exit 1
        }
    }
} else { Write-Host "?? Skipping download (using existing assemblies)" -ForegroundColor Yellow }# Step 4: Set environment variable (same as CI)
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

if ($ExcludeHeavy) {
    # Exclude long-running perf/stress traits and focus on unit tests for stability
    # Use both filter and runsettings for maximum compatibility (xUnit maps Category, some adapters use TestCategory)
    $filter = "(Category=Unit|TestCategory=Unit)&TestCategory!=Performance&TestCategory!=Stress&Category!=Performance&Category!=Stress"
    $testArgs += "--filter", $filter
    if (Test-Path "Brainarr.Tests/test.fast.runsettings") {
        $testArgs += "--settings", "Brainarr.Tests/test.fast.runsettings"
    }
}

dotnet @testArgs
$testExitCode = $LASTEXITCODE

# If flaky failures occur, retry once to stabilize CI (non-destructive)
if ($testExitCode -ne 0) {
    Write-Host "`n🔁 Retrying tests once due to failures..." -ForegroundColor Yellow
    Start-Sleep -Seconds 1
    # Try to re-run only failed tests if possible
    $trx = Get-ChildItem "TestResults" -Recurse -Filter *.trx | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($trx) {
        try {
            [xml]$doc = Get-Content $trx.FullName -Raw
            $failed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' } | ForEach-Object { $_.testName })
        } catch { $failed = @() }
        if ($failed.Count -gt 0) {
            $filter = ($failed | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
            Write-Host "Re-running failed tests:" -ForegroundColor Yellow
            $failed | ForEach-Object { Write-Host " - $_" -ForegroundColor DarkYellow }
            dotnet test "Brainarr.Tests/Brainarr.Tests.csproj" -c Release --no-build --verbosity normal --filter $filter
            if ($LASTEXITCODE -eq 0) {
                $testExitCode = 0
            } else {
                # As a last resort, re-run full suite one more time
                dotnet @testArgs
                $testExitCode = $LASTEXITCODE
            }
        } else {
            dotnet @testArgs
            $testExitCode = $LASTEXITCODE
        }
    } else {
        dotnet @testArgs
        $testExitCode = $LASTEXITCODE
    }
}

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

# Optional: Generate HTML coverage report
if ($GenerateCoverageReport) {
    try {
        $installSwitch = $InstallReportGenerator ? "-InstallTool" : ""
        & scripts/generate-coverage-report.ps1 -ResultsRoot "TestResults" -ReportDir "TestResults/CoverageReport" $installSwitch
    } catch {
        Write-Warning "Coverage report generation failed: $($_.Exception.Message)"
    }
}

Write-Host "`n🎯 Local CI test complete!" -ForegroundColor Green
Write-Host "This environment now matches your GitHub Actions CI setup." -ForegroundColor Cyan

exit $testExitCode
