#Requires -Module Pester
<#
.SYNOPSIS
    Static check: manifest.json must not contain the 'maximumVersion' key.
    The field is a no-op wildcard not required by the parity-spec and was
    removed in Phase 1 (cleanup/delete-dead-enhanced-rate-limiter).
#>

Describe 'manifest.json shape' {
    BeforeAll {
        # Resolve repo root: scripts/tests/ -> scripts/ -> repo root
        $script:repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
        $script:ManifestPath = Join-Path $script:repoRoot 'manifest.json'
        if (Test-Path $script:ManifestPath) {
            $script:ManifestContent = Get-Content $script:ManifestPath -Raw
            $script:ManifestJson = $script:ManifestContent | ConvertFrom-Json
        } else {
            $script:ManifestContent = $null
            $script:ManifestJson = $null
        }
    }

    It 'manifest.json exists' {
        Test-Path $script:ManifestPath | Should -BeTrue
    }

    It 'manifest.json does not contain maximumVersion key' {
        $script:ManifestContent | Should -Not -Match '"maximumVersion"'
    }

    It 'manifest.json is valid JSON' {
        $script:ManifestJson | Should -Not -BeNullOrEmpty
    }

    It 'manifest.json contains required minHostVersion field' {
        $script:ManifestJson.minHostVersion | Should -Not -BeNullOrEmpty
    }
}
