#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Update packaging/expected-contents.txt from the latest plugin package.

.DESCRIPTION
    Builds the plugin package via New-PluginPackage, then runs the Common
    generator to update the REQUIRED section in expected-contents.txt.

    Modes (passed through to the Common generator):
      (default)  Report differences without writing.
      -Update    Overwrite the REQUIRED section from the ZIP.
      -Check     CI mode: exit 1 if manifest drifts from the ZIP.

.EXAMPLE
    ./scripts/update-expected-contents.ps1           # report only
    ./scripts/update-expected-contents.ps1 -Update   # update manifest
    ./scripts/update-expected-contents.ps1 -Check    # CI check
#>
param(
    [switch]$Update,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $commonPath = 'ext/Lidarr.Plugin.Common'
    $generator  = Join-Path $commonPath 'scripts/generate-expected-contents.ps1'

    if (-not (Test-Path -LiteralPath $generator)) {
        throw "Generator not found at $generator. Ensure Common submodule is at >= 1fe2da1."
    }

    # Build the package to get a ZIP
    $pluginPack = Join-Path $commonPath 'tools/PluginPack.psm1'
    Import-Module (Resolve-Path -LiteralPath $pluginPack) -Force

    $zipPath = New-PluginPackage `
        -Csproj 'Brainarr.Plugin/Brainarr.Plugin.csproj' `
        -Manifest 'plugin.json' `
        -Framework net8.0 `
        -Configuration Release `
        -RequireCanonicalAbstractions | Select-Object -Last 1

    if (-not $zipPath -or -not (Test-Path -LiteralPath $zipPath)) {
        throw "Package build failed or produced no ZIP."
    }

    Write-Host "`nRunning expected-contents generator against: $zipPath" -ForegroundColor Cyan

    $genArgs = @{ ZipPath = $zipPath }
    if ($Update) { $genArgs['Update'] = $true }
    if ($Check)  { $genArgs['Check']  = $true }

    & $generator @genArgs
}
finally {
    Pop-Location
}
