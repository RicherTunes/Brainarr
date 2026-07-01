#Requires -Module Pester
<#
.SYNOPSIS
    TDD gate: CI workflow must run the full shared Common plugin lint gates
    without fallback/direct lint subsets or skip switches.
#>

BeforeAll {
    # Resolve repo root: scripts/tests/ -> scripts/ -> repo root
    $script:repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:CiPath = Join-Path $script:repoRoot '.gitea' 'workflows' 'ci.yml'
    $script:RunnerOwnedScriptPattern = '(ecosystem-parity-lint|lint-date-parsing|lint-sync-over-async|lint-test-traits|lint-doc-script-refs|lint-gitea-secret-scan)\.ps1'
    $script:RunnerSkipSwitchPattern = '-(SkipDateParsing|SkipSyncOverAsync|SkipTestTraits|SkipEcosystemParity|SkipVersionContract|SkipPluginContractTests|SkipDocRefs|SkipGiteaSecretScan)\b'
    $script:GetWorkflowNonCommentContent = {
        ((Get-Content $script:CiPath | Where-Object {
            -not $_.TrimStart().StartsWith('#')
        }) -join "`n")
    }
}

Describe 'brainarr — shared Common lint gates in Gitea ci.yml' {

    It 'ci.yml exists' {
        Test-Path $script:CiPath | Should -BeTrue -Because '.gitea/workflows/ci.yml is the primary CI workflow'
    }

    It 'ci.yml contains the shared plugin lint runner call' {
        $content = & $script:GetWorkflowNonCommentContent
        $content | Should -Match 'run-plugin-lint-gates\.ps1' `
            -Because 'the shared lint runner centralizes date, sync-over-async, trait, version-contract, doc-ref, and plugin-contract gates'
    }

    It 'ci.yml passes -RepoPath . to the shared lint runner' {
        $content = & $script:GetWorkflowNonCommentContent
        $content | Should -Match '-RepoPath\s+\.' `
            -Because 'the runner must scan the plugin repo, not the Common submodule'
    }

    It 'ci.yml passes -Mode ci to the shared lint runner' {
        $content = & $script:GetWorkflowNonCommentContent
        $content | Should -Match '-Mode\s+ci' `
            -Because 'ci mode causes the lint to fail-fast on violations'
    }

    It 'ci.yml does not call runner-owned lint scripts directly' {
        $content = & $script:GetWorkflowNonCommentContent
        $content | Should -Not -Match $script:RunnerOwnedScriptPattern `
            -Because 'direct fallback calls can silently downgrade from the full shared lint gate set'
    }

    It 'ci.yml does not pass shared runner skip switches' {
        $content = & $script:GetWorkflowNonCommentContent
        $content | Should -Not -Match $script:RunnerSkipSwitchPattern `
            -Because 'plugin CI must run the full shared Common lint gate set'
    }

    It 'ci.yml shared lint runner step appears before the verify job' {
        $content = & $script:GetWorkflowNonCommentContent
        $lintIdx = $content.IndexOf('run-plugin-lint-gates.ps1')
        $verifyIdx = $content.IndexOf('verify:')
        $lintIdx | Should -BeGreaterThan -1 -Because 'shared lint runner step must exist'
        $verifyIdx | Should -BeGreaterThan -1 -Because 'verify job must exist'
        $lintIdx | Should -BeLessThan $verifyIdx `
            -Because 'lint must run before the verify job definition (fail-fast)'
    }
}
