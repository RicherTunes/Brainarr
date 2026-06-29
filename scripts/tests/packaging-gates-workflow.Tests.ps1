#Requires -Modules Pester

<#
.SYNOPSIS
    TDD gate: Gitea CI must run Brainarr's local packaging/closure verification.

.DESCRIPTION
    Brainarr is Gitea-primary and intentionally has no .github/workflows directory.
    The packaging gate now lives in scripts/verify-local.ps1, which the Gitea verify job runs.
#>

BeforeAll {
    $script:RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:WorkflowPath = Join-Path $script:RepoRoot '.gitea\workflows\ci.yml'
    $script:VerifyLocalPath = Join-Path $script:RepoRoot 'scripts\verify-local.ps1'
}

Describe 'brainarr — Gitea packaging verification' {

    It '.gitea/workflows/ci.yml exists' {
        Test-Path $script:WorkflowPath | Should -BeTrue -Because `
            'Gitea is the primary CI workflow host'
    }

    It 'scripts/verify-local.ps1 exists' {
        Test-Path $script:VerifyLocalPath | Should -BeTrue -Because `
            'verify-local.ps1 owns build, package, closure, and deterministic test verification'
    }

    It 'Gitea verify job runs verify-local.ps1' {
        $content = Get-Content $script:WorkflowPath -Raw
        $content | Should -Match 'scripts/verify-local\.ps1' `
            -Because 'Gitea CI must run the same local verification path developers run before merge'
    }

    It 'Gitea verify job runs after lint' {
        $content = Get-Content $script:WorkflowPath -Raw
        $content | Should -Match 'needs:\s*lint' `
            -Because 'packaging verification should not start after lint has already found a policy violation'
    }
}
