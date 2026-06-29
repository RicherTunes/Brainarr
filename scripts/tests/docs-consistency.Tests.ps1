#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$check = Join-Path $repoRoot 'scripts/check-docs-consistency.ps1'

if (-not (Test-Path -LiteralPath $check)) {
    Write-Error "Missing docs consistency checker: $check"
    exit 1
}

& pwsh -NoProfile -File $check
if ($LASTEXITCODE -ne 0) {
    Write-Error "Docs consistency checker failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
