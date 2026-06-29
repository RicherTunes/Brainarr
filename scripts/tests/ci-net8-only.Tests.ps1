#Requires -Module Pester
<#
.SYNOPSIS
    Static check: CI workflow files must not reference net6.0 or 6.0.x.
    The plugin targets net8.0 only (see Brainarr.Plugin/Brainarr.Plugin.csproj).
    Added in Phase 1 cleanup/delete-dead-enhanced-rate-limiter.
#>

Describe 'CI matrix: net8.0 only' {
    BeforeAll {
        # Resolve repo root: scripts/tests/ -> scripts/ -> repo root
        $script:repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
        $script:WorkflowDir = Join-Path $script:repoRoot '.gitea' 'workflows'
        if (Test-Path $script:WorkflowDir) {
            $script:WorkflowFiles = @(Get-ChildItem -Path $script:WorkflowDir -Filter '*.yml')
        } else {
            $script:WorkflowFiles = @()
        }
    }

    It 'workflow directory exists' {
        Test-Path $script:WorkflowDir | Should -BeTrue
    }

    It 'workflow directory contains at least one yml file' {
        $script:WorkflowFiles.Count | Should -BeGreaterThan 0
    }

    It 'no workflow file contains 6.0.x' {
        $violations = $script:WorkflowFiles | Where-Object {
            (Get-Content $_.FullName -Raw) -match '6\.0\.x'
        } | Select-Object -ExpandProperty Name
        $violations | Should -BeNullOrEmpty -Because "no workflow should install net6.0 SDK (plugin is net8.0-only)"
    }

    It 'no workflow file contains net6.0' {
        $violations = $script:WorkflowFiles | Where-Object {
            (Get-Content $_.FullName -Raw) -match 'net6\.0'
        } | Select-Object -ExpandProperty Name
        $violations | Should -BeNullOrEmpty -Because "no workflow should target net6.0 framework (plugin is net8.0-only)"
    }
}
