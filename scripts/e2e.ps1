#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run the Brainarr Docker E2E smoke harness against a real Lidarr container.

.DESCRIPTION
    Wave 22b — boots a single-plugin Lidarr container (image
    pr-plugins-3.x.y.z, .NET 8) with the merged Brainarr plugin DLL mounted,
    waits for Lidarr to become healthy, and runs the DockerE2E smoke matrix.

    Brainarr is an ImportList-only plugin (HasIndexer=false,
    HasDownloadClient=false), so the matrix is 2 facts (not 4):

      * Plugin appears in /api/v1/importlist/schema
      * POST /api/v1/importlist/test with empty settings → non-5xx

    All tests skip gracefully when Docker isn't running. The harness reuses
    the per-plugin fixture in tests (BrainarrLidarrContainerFixture), which
    consumes common's lifted LidarrContainerFixture from
    Lidarr.Plugin.Common.TestKit.

    The plugin DLL must already be built with host-bridge (Lidarr.Plugin.Abstractions.dll
    sits alongside the merged DLL). The fastest way is:

        pwsh scripts/verify-local.ps1 -SkipExtract -SkipTests

    which lays the merged DLL plus Abstractions into Brainarr.Plugin/bin/.
    Without -SkipBuild this script does that for you.

.PARAMETER SkipBuild
    Skip the verify-local.ps1 build prep step. Use when the plugin DLL is
    already present at Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll alongside
    Lidarr.Plugin.Abstractions.dll.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Filter
    Optional xUnit filter override. Defaults to Category=DockerE2E.

.EXAMPLE
    pwsh scripts/e2e.ps1
    pwsh scripts/e2e.ps1 -SkipBuild
    pwsh scripts/e2e.ps1 -Filter 'FullyQualifiedName~ImportList_Test'
#>
param(
    [switch]$SkipBuild,
    [string]$Configuration = 'Release',
    [string]$Filter = 'Category=DockerE2E'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot/.."
$testProject = Join-Path $repoRoot 'Brainarr.Tests/Brainarr.Tests.csproj'

Push-Location $repoRoot
try {
    Write-Host '================================================================================' -ForegroundColor Cyan
    Write-Host '  BRAINARR DOCKER E2E HARNESS (wave 22b)' -ForegroundColor Cyan
    Write-Host '================================================================================' -ForegroundColor Cyan

    # Pre-flight: docker engine availability. We do NOT fail when Docker is
    # missing — the tests skip gracefully — but the user benefits from a clear
    # heads-up so they don't wonder why everything was skipped.
    $dockerOk = $false
    try {
        & docker info *>$null
        $dockerOk = ($LASTEXITCODE -eq 0)
    } catch {
        $dockerOk = $false
    }

    if (-not $dockerOk) {
        Write-Host '  WARNING: Docker engine is not running. E2E tests will skip.' -ForegroundColor Yellow
        Write-Host '           Start Docker Desktop and re-run to actually exercise the harness.' -ForegroundColor Yellow
    } else {
        Write-Host '  Docker engine: OK' -ForegroundColor Green
    }

    if (-not $SkipBuild) {
        Write-Host ''
        Write-Host '  [1/2] Building plugin (host-bridge)...' -ForegroundColor Cyan
        $verifyScript = Join-Path $repoRoot 'scripts/verify-local.ps1'
        if (Test-Path $verifyScript) {
            & pwsh $verifyScript -SkipExtract -SkipTests
            if ($LASTEXITCODE -ne 0) { throw 'verify-local.ps1 build prep failed' }
        } else {
            Write-Host '  verify-local.ps1 not found — falling back to dotnet build' -ForegroundColor Yellow
            & dotnet build (Join-Path $repoRoot 'Brainarr.Plugin/Brainarr.Plugin.csproj') -c $Configuration --nologo -m:1
            if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
        }
    } else {
        Write-Host '  [1/2] Skipping build (-SkipBuild)' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host "  [2/2] Running E2E tests (filter: $Filter)..." -ForegroundColor Cyan

    & dotnet test $testProject `
        -c $Configuration `
        -v normal `
        -m:1 `
        -p:PluginPackagingDisable=true `
        --filter $Filter

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test exited with code $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host '  E2E harness complete.' -ForegroundColor Green
}
finally {
    Pop-Location
}
