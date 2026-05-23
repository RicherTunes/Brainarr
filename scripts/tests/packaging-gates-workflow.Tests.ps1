#Requires -Modules Pester

<#
.SYNOPSIS
    Phase 1.5 — TDD gate: .github/workflows/packaging-gates.yml must delegate to
    Common's reusable workflow and must pin that workflow to a specific commit SHA
    (not @main).

.DESCRIPTION
    Asserts three properties of the workflow file:
    1. The file exists.
    2. It contains a `uses:` reference to Common's reusable packaging-gates workflow.
    3. The SHA pin is present (i.e. the @ref is a 40-hex-character commit SHA, NOT "@main",
       "@master", or a semver tag without a SHA).
#>

BeforeAll {
    $script:WorkflowPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) `
        '.github\workflows\packaging-gates.yml'
}

Describe 'brainarr — .github/workflows/packaging-gates.yml' {

    It 'exists' {
        Test-Path $script:WorkflowPath | Should -BeTrue -Because `
            'packaging-gates.yml must be present so Common CI gates apply to this repo'
    }

    It 'delegates to the Common reusable packaging-gates workflow via uses:' {
        $content = Get-Content $script:WorkflowPath -Raw
        $content | Should -Match 'uses:\s*RicherTunes/Lidarr\.Plugin\.Common/\.github/workflows/packaging-gates\.yml' `
            -Because 'the workflow must call Common''s reusable workflow, not duplicate its logic'
    }

    It 'pins the Common workflow to a commit SHA (no @main / @master / bare tag)' {
        $content = Get-Content $script:WorkflowPath -Raw

        # Extract the @ref part of the uses: line.
        if ($content -match 'packaging-gates\.yml@([^\s]+)') {
            $ref = $Matches[1]
            # A valid commit SHA is exactly 40 hex characters.
            $ref | Should -Match '^[0-9a-f]{40}$' `
                -Because "the workflow must be pinned to a commit SHA for supply-chain safety, got '@$ref'"
        }
        else {
            Set-ItResult -Skipped -Because 'uses: line not found — covered by the previous test'
        }
    }
}
