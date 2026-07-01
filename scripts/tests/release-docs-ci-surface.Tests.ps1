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
    It 'has no GitHub workflow directory' {
        Test-Path $script:GitHubWorkflows | Should -BeFalse -Because `
            'Brainarr is Gitea-primary and GitHub is currently a mirror only'
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
