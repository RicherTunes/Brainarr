#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Syncs ext-common-sha.txt with the actual Common submodule commit SHA.

.DESCRIPTION
    Reads the current commit SHA from ext/Lidarr.Plugin.Common submodule
    and writes it to ext-common-sha.txt. This prevents drift between the
    pinned submodule and the recorded SHA.

.EXAMPLE
    ./scripts/sync-ext-common-sha.ps1
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$submodulePath = Join-Path $repoRoot 'ext/Lidarr.Plugin.Common'
$shaFile = Join-Path $repoRoot 'ext-common-sha.txt'

if (-not (Test-Path $submodulePath)) {
    Write-Error "Submodule not found at: $submodulePath"
    exit 1
}

Push-Location $submodulePath
try {
    $sha = git rev-parse HEAD
    if (-not $sha) {
        Write-Error "Failed to get submodule commit SHA"
        exit 1
    }
    $sha = $sha.Trim()
}
finally {
    Pop-Location
}

# Write SHA without quotes or extra whitespace
Set-Content -Path $shaFile -Value $sha -NoNewline -Encoding UTF8

Write-Host "Synced ext-common-sha.txt to: $sha" -ForegroundColor Green
