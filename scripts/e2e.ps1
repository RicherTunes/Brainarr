#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run the Brainarr Docker E2E smoke harness against a real Lidarr container.

.DESCRIPTION
    Thin shim — delegates to the shared runner in Lidarr.Plugin.Common.

    Brainarr is an ImportList-only plugin (HasIndexer=false,
    HasDownloadClient=false), so the matrix is 2 facts (not 4):

      * Plugin appears in /api/v1/importlist/schema
      * POST /api/v1/importlist/test with empty settings -> non-5xx

    All tests skip gracefully when Docker isn't running.

.PARAMETER SkipBuild
    Skip the verify-local.ps1 build prep step.

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

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path "$PSScriptRoot/.."
Set-Location $repoRoot

& "$PSScriptRoot/../ext/Lidarr.Plugin.Common/scripts/e2e-local-runner.ps1" `
    -PluginName 'Brainarr' `
    -TestProject 'Brainarr.Tests/Brainarr.Tests.csproj' `
    -SkipBuild:$SkipBuild `
    -Configuration $Configuration `
    -Filter $Filter `
    -ExtraBuildArgs '-m:1' `
    -FallbackBuildOnMissingVerify

exit $LASTEXITCODE
