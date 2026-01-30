<#
.SYNOPSIS
    Runs Brainarr unit tests with proper packaging flags.

.DESCRIPTION
    This script ensures tests run in "unmerged" mode (PluginPackagingDisable=true)
    so that ILRepack internalization doesn't break type identity in tests.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug

.PARAMETER Filter
    Optional test filter expression (e.g., "Category=Integration")

.PARAMETER Coverage
    Enable code coverage collection

.PARAMETER ExcludePackaging
    Exclude packaging tests (only valid after ILRepack packaging). Default: true

.PARAMETER ExcludeSlow
    Exclude slow/timing-sensitive tests. Default: true

.PARAMETER Verbose
    Enable verbose output

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 -Filter "Category=Integration"
    ./scripts/test.ps1 -ExcludePackaging:$false -Coverage
#>

param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$ExcludePackaging = $true,  # Default to true - only valid after ILRepack
    [switch]$ExcludeSlow = $true,       # Default to true - timing-sensitive tests
    [switch]$Coverage = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

Write-Host "[TEST] Brainarr Unit Test Runner (UNMERGED MODE)" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "[INFO] Mode: Unit tests with PluginPackagingDisable=true" -ForegroundColor Gray
Write-Host "[INFO] This tests unmerged assemblies. For packaging tests, use test-packaging.ps1" -ForegroundColor Gray

# Avoid intermittent file-lock issues when building shared projects (notably submodules) on Windows.
# These knobs trade a bit of build speed for determinism.
$env:DOTNET_CLI_DISABLE_BUILD_SERVERS = "1"
$env:MSBUILDDISABLENODEREUSE = "1"

# Build excluded categories summary
$excludedCategories = @()
if ($ExcludePackaging) { $excludedCategories += "Packaging" }
if ($ExcludeSlow) { $excludedCategories += "Slow" }
if ($excludedCategories.Count -gt 0) {
    Write-Host "[INFO] Excluded categories: $($excludedCategories -join ', ')" -ForegroundColor Yellow
}

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$TestProjects = @(
    (Join-Path $ProjectRoot "Brainarr.Tests/Brainarr.Tests.csproj"),
    (Join-Path $ProjectRoot "tests/Brainarr.Providers.OpenAI.Tests/Brainarr.Providers.OpenAI.Tests.csproj")
)
$OutputDir = Join-Path $ProjectRoot "TestResults"

# Ensure output directory exists
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Remove stale TRX files so the final summary reflects this run.
Get-ChildItem -Path $OutputDir -Filter "*.trx" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "[INFO] Test Projects:" -ForegroundColor Gray
foreach ($proj in $TestProjects) {
    if (Test-Path $proj) {
        Write-Host "  - $proj" -ForegroundColor Gray
    }
}
Write-Host "[INFO] Output Directory: $OutputDir" -ForegroundColor Gray
Write-Host ""

$overallExitCode = 0

foreach ($TestProject in $TestProjects) {
    if (!(Test-Path $TestProject)) {
        Write-Host "[SKIP] Project not found: $TestProject" -ForegroundColor Yellow
        continue
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($TestProject)
    Write-Host "[BUILD] Building $projectName (unmerged mode)..." -ForegroundColor Yellow

    # Build with PluginPackagingDisable=true to avoid ILRepack internalization issues
    $buildArgs = @(
        "build", $TestProject,
        "--configuration", $Configuration,
        "-p:PluginPackagingDisable=true",
        "-p:RunAnalyzersDuringBuild=false",
        "-p:EnableNETAnalyzers=false",
        "-p:TreatWarningsAsErrors=false",
        "/m:1",
        "/p:BuildInParallel=false",
        "/p:UseSharedCompilation=false"
    )

    if ($Verbose) {
        $buildArgs += @("--verbosity", "detailed")
    } else {
        $buildArgs += @("--verbosity", "minimal")
    }

    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Build failed for $projectName!" -ForegroundColor Red
        $overallExitCode = 1
        continue
    }
    Write-Host "[OK] Build successful!" -ForegroundColor Green
    Write-Host ""

    # Run tests
    Write-Host "[TEST] Running $projectName tests..." -ForegroundColor Yellow

    $testArgs = @(
        "test", $TestProject,
        "--configuration", $Configuration,
        "--no-build",
        "--logger", "trx;LogFileName=$projectName.trx",
        "--results-directory", $OutputDir
    )

    # Apply filters
    $effectiveFilter = $Filter
    if ($ExcludePackaging) {
        $packagingFilter = "Category!=Packaging"
        if ($effectiveFilter) {
            $effectiveFilter = "($effectiveFilter) & ($packagingFilter)"
        } else {
            $effectiveFilter = $packagingFilter
        }
    }
    if ($ExcludeSlow) {
        $slowFilter = "Category!=Slow"
        if ($effectiveFilter) {
            $effectiveFilter = "($effectiveFilter) & ($slowFilter)"
        } else {
            $effectiveFilter = $slowFilter
        }
    }

    if ($effectiveFilter) {
        Write-Host "[INFO] Test filter: $effectiveFilter" -ForegroundColor Gray
        $testArgs += @("--filter", $effectiveFilter)
    }

    if ($Coverage) {
        $testArgs += @("--collect", "XPlat Code Coverage")
    }

    if ($Verbose) {
        $testArgs += @("--verbosity", "detailed")
    } else {
        $testArgs += @("--verbosity", "normal")
    }

    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        $overallExitCode = 1
    }

    Write-Host ""
}

# Parse and display summary
$trxFiles = Get-ChildItem -Path $OutputDir -Filter "*.trx" -ErrorAction SilentlyContinue
if ($trxFiles) {
    $totalTests = 0
    $totalPassed = 0
    $totalFailed = 0
    $totalSkipped = 0

    foreach ($trxFile in $trxFiles) {
        try {
            [xml]$trxXml = Get-Content $trxFile.FullName
            $counters = $trxXml.TestRun.ResultSummary.Counters
            $fileTotal = [int]$counters.total
            $fileExecuted = [int]$counters.executed
            $filePassed = [int]$counters.passed
            $fileFailed = [int]$counters.failed

            $totalTests += $fileTotal
            $totalPassed += $filePassed
            $totalFailed += $fileFailed

            # Some adapters (notably xUnit) don't populate notExecuted; fall back to (total - executed).
            $fileNotExecuted = 0
            try { $fileNotExecuted = [int]$counters.notExecuted } catch { }
            $fileSkipped = if ($fileNotExecuted -gt 0) { $fileNotExecuted } else { [Math]::Max(0, $fileTotal - $fileExecuted) }
            $totalSkipped += $fileSkipped
        } catch {
            # Ignore parse errors
        }
    }

    Write-Host "Test Results Summary:" -ForegroundColor Cyan
    Write-Host "  Total:   $totalTests" -ForegroundColor White
    Write-Host "  Passed:  $totalPassed" -ForegroundColor Green
    Write-Host "  Failed:  $totalFailed" -ForegroundColor $(if ($totalFailed -gt 0) { "Red" } else { "Gray" })
    Write-Host "  Skipped: $totalSkipped" -ForegroundColor Yellow

    $passRate = if ($totalTests -gt 0) { [math]::Round(($totalPassed / $totalTests) * 100, 2) } else { 0 }
    Write-Host "  Pass Rate: $passRate%" -ForegroundColor $(if ($passRate -ge 80) { "Green" } elseif ($passRate -ge 60) { "Yellow" } else { "Red" })
}

Write-Host ""
Write-Host "Results saved to: $OutputDir" -ForegroundColor Gray

if ($overallExitCode -eq 0) {
    Write-Host "[OK] All tests passed!" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Some tests failed!" -ForegroundColor Red
}

exit $overallExitCode
