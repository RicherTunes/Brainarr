#Requires -Module Pester
<#
.SYNOPSIS
    TDD gate: CI workflow must run the shared Common plugin lint gates.
    Added in Phase 1.5 CI/CD standardization (ci-cd-agent).
#>

BeforeAll {
    # Resolve repo root: scripts/tests/ -> scripts/ -> repo root
    $script:repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:CiPath = Join-Path $script:repoRoot '.gitea' 'workflows' 'ci.yml'
}

Describe 'brainarr — shared Common lint gates in Gitea ci.yml' {

    It 'ci.yml exists' {
        Test-Path $script:CiPath | Should -BeTrue -Because '.gitea/workflows/ci.yml is the primary CI workflow'
    }

    It 'ci.yml contains the shared plugin lint runner call' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match 'run-plugin-lint-gates\.ps1' `
            -Because 'the shared lint runner centralizes date, sync-over-async, trait, version-contract, doc-ref, and plugin-contract gates'
    }

    It 'ci.yml passes -RepoPath . to the shared lint runner' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match '-RepoPath\s+\.' `
            -Because 'the runner must scan the plugin repo, not the Common submodule'
    }

    It 'ci.yml passes -Mode ci to the shared lint runner' {
        $content = Get-Content $script:CiPath -Raw
        $content | Should -Match '-Mode\s+ci' `
            -Because 'ci mode causes the lint to fail-fast on violations'
    }

    It 'ci.yml shared lint runner step appears before the verify job' {
        $content = Get-Content $script:CiPath -Raw
        $lintIdx = $content.IndexOf('run-plugin-lint-gates.ps1')
        $verifyIdx = $content.IndexOf('verify:')
        $lintIdx | Should -BeGreaterThan -1 -Because 'shared lint runner step must exist'
        $verifyIdx | Should -BeGreaterThan -1 -Because 'verify job must exist'
        $lintIdx | Should -BeLessThan $verifyIdx `
            -Because 'lint must run before the verify job definition (fail-fast)'
    }
}
