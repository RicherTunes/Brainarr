#Requires -Module Pester
<#
.SYNOPSIS
    TDD gate: CI workflow must contain the ecosystem-parity-lint step with -Check VersionContract.
    Added in Phase 1.5 CI/CD standardization (ci-cd-agent).
#>

BeforeAll {
    # Resolve repo root: scripts/tests/ -> scripts/ -> repo root
    $script:repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:CiPath = Join-Path $script:repoRoot '.github' 'workflows' 'ci.yml'
}

Describe 'brainarr — ecosystem parity lint in ci.yml' {

    It 'ci.yml exists' {
        Test-Path $script:CiPath | Should -BeTrue -Because 'ci.yml is the primary CI workflow'
    }

    It 'ci.yml contains the ecosystem-parity-lint.ps1 call' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match 'ecosystem-parity-lint\.ps1' `
            -Because 'the VersionContract lint step must be present to catch commonVersion drift on PRs'
    }

    It 'ci.yml passes -Check VersionContract to ecosystem-parity-lint' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match '-Check\s+VersionContract' `
            -Because 'the step must narrow the check scope to VersionContract'
    }

    It 'ci.yml passes -Mode ci to ecosystem-parity-lint' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match '-Mode\s+ci' `
            -Because 'ci mode causes the lint to fail-fast on violations'
    }

    It 'ci.yml ecosystem-parity-lint step appears before the build step' {
        $content = Get-Content $script:CiPath -Raw
        $ecoIdx = $content.IndexOf('ecosystem-parity-lint.ps1')
        # "Build" step keyword - look for dotnet build or the build job header
        $buildIdx = $content.IndexOf('dotnet build')
        if ($buildIdx -lt 0) { $buildIdx = $content.IndexOf('name: Build') }
        $ecoIdx | Should -BeGreaterThan -1 -Because 'ecosystem-parity-lint step must exist'
        $buildIdx | Should -BeGreaterThan -1 -Because 'a build step must exist'
        $ecoIdx | Should -BeLessThan $buildIdx `
            -Because 'parity lint must run before the build (fail-fast)'
    }
}
