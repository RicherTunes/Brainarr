$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$gitmodulesPath = Join-Path $repoRoot '.gitmodules'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

Assert-Condition (Test-Path -LiteralPath $workflowPath) "Missing GitHub CI mirror workflow: $workflowPath"
Assert-Condition (Test-Path -LiteralPath $gitmodulesPath) "Missing .gitmodules: $gitmodulesPath"

$workflow = Get-Content -LiteralPath $workflowPath -Raw
$gitmodules = Get-Content -LiteralPath $gitmodulesPath -Raw
$nonCommentWorkflow = ((Get-Content -LiteralPath $workflowPath) | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"

if ($gitmodules -match '192\.168\.2\.59:3001/RicherTunes/Lidarr\.Plugin\.Common\.git') {
    Assert-Condition ($nonCommentWorkflow -notmatch 'submodules:\s*recursive') `
        'GitHub CI must not use actions/checkout submodules: recursive when Common points at LAN-only Gitea.'
    Assert-Condition ($nonCommentWorkflow -match 'submodules:\s*false') `
        'GitHub CI should checkout with submodules: false, then initialize Common explicitly.'
    Assert-Condition ($nonCommentWorkflow -match 'git config\s+url\.https://github\.com/RicherTunes/Lidarr\.Plugin\.Common\.git\.insteadOf\s+http://192\.168\.2\.59:3001/RicherTunes/Lidarr\.Plugin\.Common\.git') `
        'GitHub CI must rewrite the LAN-only Common submodule URL to the GitHub mirror before submodule update.'
    Assert-Condition ($nonCommentWorkflow -match 'git submodule update --init -- ext/Lidarr\.Plugin\.Common') `
        'GitHub CI must initialize only ext/Lidarr.Plugin.Common after the URL rewrite.'
    Assert-Condition ($nonCommentWorkflow -notmatch 'git submodule update[^\r\n]*--depth') `
        'GitHub CI must not shallow-fetch Common; plugin gitlinks may point at Gitea raw-history commits.'
}

Write-Host 'PASS: GitHub Common submodule init contract'
