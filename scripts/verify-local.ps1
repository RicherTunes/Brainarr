#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run local CI verification for Brainarr.

.DESCRIPTION
    Thin caller that passes Brainarr-specific configuration to the shared
    local-ci.ps1 runner from Lidarr.Plugin.Common.

.EXAMPLE
    pwsh scripts/verify-local.ps1                  # Full pipeline
    pwsh scripts/verify-local.ps1 -SkipExtract     # Fast rerun (cached assemblies)
    pwsh scripts/verify-local.ps1 -SkipTests       # Build + closure only
    pwsh scripts/verify-local.ps1 -NoRestore       # Skip restore (fast iteration)
    pwsh scripts/verify-local.ps1 -IncludeSmoke    # + Docker smoke test
#>
param(
    [switch]$SkipExtract,
    [switch]$SkipTests,
    [switch]$NoRestore,
    [switch]$IncludeSmoke
)

$ErrorActionPreference = 'Stop'

# Resolve repo root (one level up from scripts/)
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    $config = @{
        RepoName             = 'Brainarr'
        SolutionFile         = 'Brainarr.sln'
        PluginCsproj         = 'Brainarr.Plugin/Brainarr.Plugin.csproj'
        ManifestPath         = 'plugin.json'
        MainDll              = 'Lidarr.Plugin.Brainarr.dll'
        HostAssembliesPath   = 'ext/Lidarr-docker/_output/net8.0'
        CommonPath           = 'ext/Lidarr.Plugin.Common'
        LidarrDockerVersion  = 'pr-plugins-3.1.2.4913'
        # -m:1 required: Brainarr has project dependency ordering issues with parallel builds
        BuildFlags           = @('-p:LidarrPath={HOST_PATH}', '-p:PluginPackagingDisable=true', '-m:1')
        TestProjects         = @('Brainarr.Tests/Brainarr.Tests.csproj')
        ExpectedContentsFile = 'packaging/expected-contents.txt'
        WarningBudget        = 80
        WarningBudgetEnforce = $false
    }

    $runner = Join-Path $config.CommonPath 'scripts/local-ci.ps1'
    if (-not (Test-Path -LiteralPath $runner)) {
        Write-Host "ERROR: Shared runner not found at: $runner" -ForegroundColor Red
        Write-Host "  Ensure Common submodule is up to date:" -ForegroundColor Yellow
        Write-Host "  git submodule update --init ext/Lidarr.Plugin.Common" -ForegroundColor Yellow
        exit 1
    }

    $runnerArgs = @{ Config = $config }
    if ($SkipExtract)  { $runnerArgs['SkipExtract']  = $true }
    if ($SkipTests)    { $runnerArgs['SkipTests']    = $true }
    if ($NoRestore)    { $runnerArgs['NoRestore']    = $true }
    if ($IncludeSmoke) { $runnerArgs['IncludeSmoke'] = $true }

    & $runner @runnerArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
