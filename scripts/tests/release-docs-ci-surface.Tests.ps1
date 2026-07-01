#Requires -Module Pester

BeforeAll {
    $script:RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:GitHubWorkflows = Join-Path $script:RepoRoot '.github\workflows'
    $script:ReleaseFiles = @(
        'docs\RELEASE_PROCESS.md',
        'docs\RELEASE_CHECKLIST.md',
        'docs\ci-stability-guide.md',
        'docs\DEPLOYMENT.md',
        'docs\TESTING_GUIDE.md',
        'scripts\new-release.ps1',
        'scripts\new-release.sh'
    ) | ForEach-Object { Join-Path $script:RepoRoot $_ }
}

Describe 'brainarr — release docs match current CI surface' {
    It 'has a GitHub Actions CI mirror (dual-platform parity)' {
        Test-Path $script:GitHubWorkflows | Should -BeTrue -Because `
            'Brainarr keeps a GitHub Actions mirror of the Gitea CI gate so the same checks build once Actions billing is restored'
        $ciMirror = Join-Path $script:GitHubWorkflows 'ci.yml'
        Test-Path $ciMirror | Should -BeTrue -Because `
            'the GitHub mirror entrypoint is .github/workflows/ci.yml, kept in sync with .gitea/workflows/ci.yml'

        $content = Get-Content $ciMirror -Raw
        $nonCommentContent = ((Get-Content $ciMirror | Where-Object {
            -not $_.TrimStart().StartsWith('#')
        }) -join "`n")
        $guard = "if: `${{ github.server_url == 'https://github.com' }}"

        $content.Contains($guard) | Should -BeTrue -Because `
            'GitHub mirror jobs must be guarded so Gitea remains the primary merge gate'
        ([regex]::Matches($content, [regex]::Escape($guard))).Count | Should -BeGreaterOrEqual 3 -Because `
            'secret-scan, lint, and verify mirror jobs must all be GitHub-only'
        $nonCommentContent | Should -Match 'run-plugin-lint-gates\.ps1' -Because `
            'the GitHub mirror must use the same shared lint runner as Gitea'
        $nonCommentContent | Should -Match 'repin-common-submodule\.sh\s+--verify-only' -Because `
            'the GitHub mirror must keep the Common submodule pin guard'
        $nonCommentContent | Should -Match 'gitleaks\s+detect' -Because `
            'the GitHub mirror must keep the secret-scan gate'
        $nonCommentContent | Should -Match 'scripts[/\\]verify-local\.ps1' -Because `
            'the GitHub mirror must run the same local verification wrapper'
        $nonCommentContent | Should -Not -Match 'Invoke-FallbackGate|ecosystem-parity-lint\.ps1|lint-date-parsing\.ps1|lint-sync-over-async\.ps1' -Because `
            'the GitHub mirror must not keep fallback/direct lint subsets'
    }

    It 'does not document or call GitHub Actions release automation from active release docs/scripts' {
        foreach ($path in $script:ReleaseFiles) {
            Test-Path $path | Should -BeTrue
            $content = Get-Content $path -Raw

            $content | Should -Not -Match 'GitHub Actions' -Because "$path must not point users at inactive GitHub workflows"
            $content | Should -Not -Match 'gh run list' -Because "$path must not ask users to inspect inactive GitHub workflows"
            $content | Should -Not -Match 'github\.com/RicherTunes/Brainarr/actions' -Because "$path must not link inactive workflow logs"
            $content | Should -Not -Match 'trigger release automation' -Because "$path must not promise automation that does not exist"
        }
    }
}
