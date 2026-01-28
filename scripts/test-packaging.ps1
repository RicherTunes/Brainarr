<#
.SYNOPSIS
    Runs Brainarr packaging tests against merged plugin assembly.

.DESCRIPTION
    This script runs tests that validate ILRepack packaging and assembly isolation.
    These tests REQUIRE a merged plugin package and will fail in unmerged mode.

    Use this after building with PluginPackagingDisable=false or after New-PluginPackage.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release

.PARAMETER PackagePath
    Path to the plugin package (.zip) or extracted plugin directory.
    If not specified, searches for latest package in build output.

.PARAMETER RequirePackage
    If set, fails immediately if no package is found (useful for CI).
    Default: false (skips tests if no package found)

.PARAMETER Verbose
    Enable verbose output

.EXAMPLE
    ./scripts/test-packaging.ps1
    ./scripts/test-packaging.ps1 -PackagePath "./artifacts/Brainarr-1.0.0.zip"
    ./scripts/test-packaging.ps1 -RequirePackage  # For CI - fails if no package

.NOTES
    Environment variables:
    - REQUIRE_PACKAGE_TESTS: If "true", equivalent to -RequirePackage
    - PLUGIN_ASSEMBLY_PATH: Direct path to merged Lidarr.Plugin.Brainarr.dll
#>

param(
    [string]$Configuration = "Release",
    [string]$PackagePath = "",
    [switch]$RequirePackage = $false,
    [switch]$Verbose = $false
)

$ErrorActionPreference = "Stop"

Write-Host "[PACKAGING] Brainarr Packaging Test Runner" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan

$ProjectRoot = Split-Path -Parent $PSScriptRoot

# Check environment variable override
if ($env:REQUIRE_PACKAGE_TESTS -eq "true") {
    $RequirePackage = $true
    Write-Host "[INFO] REQUIRE_PACKAGE_TESTS=true, will fail if no package found" -ForegroundColor Yellow
}

# Determine plugin assembly path
$PluginAssemblyPath = $env:PLUGIN_ASSEMBLY_PATH

if (-not $PluginAssemblyPath) {
    # Try to find package or built assembly
    if ($PackagePath) {
        if (Test-Path $PackagePath) {
            if ($PackagePath.EndsWith(".zip")) {
                # Extract to temp and find DLL
                $extractDir = Join-Path $env:TEMP "brainarr-test-$(Get-Random)"
                Write-Host "[INFO] Extracting package to $extractDir" -ForegroundColor Gray
                Expand-Archive -Path $PackagePath -DestinationPath $extractDir -Force
                $PluginAssemblyPath = Get-ChildItem -Path $extractDir -Filter "Lidarr.Plugin.Brainarr.dll" -Recurse | Select-Object -First 1 -ExpandProperty FullName
            } else {
                # Assume it's a directory
                $PluginAssemblyPath = Join-Path $PackagePath "Lidarr.Plugin.Brainarr.dll"
            }
        }
    } else {
        # Search for latest package in common locations
        $searchPaths = @(
            (Join-Path $ProjectRoot "Brainarr.Plugin/artifacts/packages"),
            (Join-Path $ProjectRoot "artifacts/packages"),
            (Join-Path $ProjectRoot "artifacts"),
            (Join-Path $ProjectRoot "bin/$Configuration"),
            (Join-Path $ProjectRoot "Brainarr.Plugin/bin/$Configuration/net8.0"),
            (Join-Path $ProjectRoot "Brainarr.Plugin/bin/$Configuration/net6.0")
        )

        foreach ($searchPath in $searchPaths) {
            if (Test-Path $searchPath) {
                # Look for zip packages first (case-insensitive for Linux compatibility)
                # PowerShell -like is case-insensitive by default
                $package = Get-ChildItem -Path $searchPath -Filter "*.zip" -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like "brainarr*" } |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 1

                if ($package) {
                    $extractDir = Join-Path $env:TEMP "brainarr-test-$(Get-Random)"
                    Write-Host "[INFO] Found package: $($package.FullName)" -ForegroundColor Gray
                    Write-Host "[INFO] Extracting to $extractDir" -ForegroundColor Gray
                    Expand-Archive -Path $package.FullName -DestinationPath $extractDir -Force
                    $PluginAssemblyPath = Get-ChildItem -Path $extractDir -Filter "Lidarr.Plugin.Brainarr.dll" -Recurse | Select-Object -First 1 -ExpandProperty FullName
                    break
                }

                # Look for merged DLL directly
                $dll = Get-ChildItem -Path $searchPath -Filter "Lidarr.Plugin.Brainarr.dll" -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 1

                if ($dll) {
                    $PluginAssemblyPath = $dll.FullName
                    break
                }
            }
        }
    }
}

# Validate we have an assembly to test
if (-not $PluginAssemblyPath -or -not (Test-Path $PluginAssemblyPath)) {
    if ($RequirePackage) {
        Write-Host "[ERROR] No plugin package/assembly found and REQUIRE_PACKAGE_TESTS is set!" -ForegroundColor Red
        Write-Host "[ERROR] Build with PluginPackagingDisable=false first, or provide -PackagePath" -ForegroundColor Red
        exit 1
    } else {
        Write-Host "[SKIP] No plugin package/assembly found. Skipping packaging tests." -ForegroundColor Yellow
        Write-Host "[INFO] To require these tests, set REQUIRE_PACKAGE_TESTS=true or use -RequirePackage" -ForegroundColor Gray
        exit 0
    }
}

Write-Host "[INFO] Testing assembly: $PluginAssemblyPath" -ForegroundColor Green

# Verify it's actually a merged assembly (should have internalized dependencies)
$assemblyInfo = [System.Reflection.AssemblyName]::GetAssemblyName($PluginAssemblyPath)
Write-Host "[INFO] Assembly version: $($assemblyInfo.Version)" -ForegroundColor Gray

# ============================================================================
# ARTIFACT FRESHNESS VALIDATION
# Prevents false-green from testing stale packages that don't match checkout
# ============================================================================
$pluginDir = Split-Path -Parent $PluginAssemblyPath
$pluginJsonPath = Join-Path $pluginDir "plugin.json"

if (Test-Path $pluginJsonPath) {
    try {
        $pluginJson = Get-Content $pluginJsonPath -Raw | ConvertFrom-Json
        $packageVersion = $pluginJson.version
        Write-Host "[INFO] Package plugin.json version: $packageVersion" -ForegroundColor Gray

        # Query MSBuild for authoritative version (handles VERSION files, Directory.Build.props, etc.)
        $csprojPath = Join-Path $ProjectRoot "Brainarr.Plugin/Brainarr.Plugin.csproj"
        if (Test-Path $csprojPath) {
            $msbuildVersion = (dotnet msbuild $csprojPath -getProperty:Version -verbosity:quiet 2>$null)
            if (-not $msbuildVersion) {
                $msbuildVersion = (dotnet msbuild $csprojPath -getProperty:AssemblyInformationalVersion -verbosity:quiet 2>$null)
            }

            if ($msbuildVersion) {
                $expectedVersion = $msbuildVersion.Trim()
                Write-Host "[INFO] MSBuild expected version: $expectedVersion" -ForegroundColor Gray

                # Compare versions (strip any build metadata for comparison)
                $pkgVersionBase = ($packageVersion -split '\+')[0]
                $expVersionBase = ($expectedVersion -split '\+')[0]

                if ($pkgVersionBase -ne $expVersionBase) {
                    Write-Host "[WARN] Version mismatch detected!" -ForegroundColor Yellow
                    Write-Host "[WARN]   Package version: $packageVersion" -ForegroundColor Yellow
                    Write-Host "[WARN]   Expected version: $expectedVersion" -ForegroundColor Yellow

                    if ($RequirePackage) {
                        Write-Host "[ERROR] Stale package detected! Rebuild with current checkout." -ForegroundColor Red
                        Write-Host "[ERROR] Run: dotnet build -c $Configuration && ./scripts/package.ps1" -ForegroundColor Red
                        exit 1
                    } else {
                        Write-Host "[WARN] Continuing anyway (use -RequirePackage to fail on mismatch)" -ForegroundColor Yellow
                    }
                } else {
                    Write-Host "[OK] Package version matches checkout" -ForegroundColor Green
                }
            } else {
                Write-Host "[WARN] Could not query MSBuild for version" -ForegroundColor Yellow
            }
        }

        # Check git SHA - REQUIRED if present in package (hard fail on mismatch)
        $currentSha = (git -C $ProjectRoot rev-parse HEAD 2>$null)
        if ($pluginJson.PSObject.Properties['gitSha'] -and $pluginJson.gitSha -and $currentSha) {
            $packageSha = $pluginJson.gitSha.Trim()
            $currentSha = $currentSha.Trim()

            # Compare first 8 chars minimum (short SHA)
            $compareLen = [Math]::Min(8, [Math]::Min($packageSha.Length, $currentSha.Length))
            $pkgShaShort = $packageSha.Substring(0, $compareLen)
            $curShaShort = $currentSha.Substring(0, $compareLen)

            if ($pkgShaShort -ne $curShaShort) {
                Write-Host "[WARN] Git SHA mismatch!" -ForegroundColor Yellow
                Write-Host "[WARN]   Package SHA: $packageSha" -ForegroundColor Yellow
                Write-Host "[WARN]   Current SHA: $($currentSha.Substring(0, 12))..." -ForegroundColor Yellow

                if ($RequirePackage) {
                    Write-Host "[ERROR] Package was built from different commit! Rebuild required." -ForegroundColor Red
                    exit 1
                }
            } else {
                Write-Host "[OK] Package git SHA matches checkout" -ForegroundColor Green
            }
        } elseif ($RequirePackage -and -not $pluginJson.PSObject.Properties['gitSha']) {
            Write-Host "[WARN] Package missing gitSha field - cannot verify commit match" -ForegroundColor Yellow
            Write-Host "[WARN] Update PluginPackaging.targets to embed gitSha in plugin.json" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "[WARN] Could not validate package freshness: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] No plugin.json found in package - cannot validate freshness" -ForegroundColor Yellow
}

# Set environment variable for tests to use
$env:PLUGIN_ASSEMBLY_PATH = $PluginAssemblyPath

# Build test project (needs to reference the assembly)
$TestProject = Join-Path $ProjectRoot "Brainarr.Tests/Brainarr.Tests.csproj"

Write-Host ""
Write-Host "[BUILD] Building test project..." -ForegroundColor Yellow

$buildArgs = @(
    "build", $TestProject,
    "--configuration", $Configuration,
    "-p:PluginPackagingDisable=true",  # Tests themselves run unmerged
    "-p:RunAnalyzersDuringBuild=false",
    "-p:EnableNETAnalyzers=false",
    "-p:TreatWarningsAsErrors=false"
)

if ($Verbose) {
    $buildArgs += @("--verbosity", "detailed")
} else {
    $buildArgs += @("--verbosity", "minimal")
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "[OK] Build successful!" -ForegroundColor Green
Write-Host ""

# Run packaging tests only
Write-Host "[TEST] Running Packaging tests..." -ForegroundColor Yellow

$testFilter = "Category=Packaging"
Write-Host "[INFO] Test filter: $testFilter" -ForegroundColor Gray

$testArgs = @(
    "test", $TestProject,
    "--configuration", $Configuration,
    "--no-build",
    "--filter", $testFilter,
    "--logger", "trx;LogFileName=Packaging.trx",
    "--results-directory", (Join-Path $ProjectRoot "TestResults")
)

if ($Verbose) {
    $testArgs += @("--verbosity", "detailed")
} else {
    $testArgs += @("--verbosity", "normal")
}

& dotnet @testArgs
$testExitCode = $LASTEXITCODE

Write-Host ""
if ($testExitCode -eq 0) {
    Write-Host "[OK] All packaging tests passed!" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Some packaging tests failed!" -ForegroundColor Red
}

# Cleanup temp extraction directory if created
if ($extractDir -and (Test-Path $extractDir)) {
    Remove-Item -Path $extractDir -Recurse -Force -ErrorAction SilentlyContinue
}

exit $testExitCode
