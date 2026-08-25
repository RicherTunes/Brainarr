#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Fail {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Ok {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Green
}

function Get-JsonString {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return $null
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

try {
    $plugin = Get-Content 'plugin.json' -Raw | ConvertFrom-Json
    $manifest = Get-Content 'manifest.json' -Raw | ConvertFrom-Json
} catch {
    Fail 'Unable to parse plugin.json or manifest.json'
}

$pluginVersion = Get-JsonString -Object $plugin -Names @('version')
$pluginMinVersion = Get-JsonString -Object $plugin -Names @('minHostVersion', 'minimumVersion')
$manifestVersion = Get-JsonString -Object $manifest -Names @('version')
$manifestMinVersion = Get-JsonString -Object $manifest -Names @('minHostVersion', 'minimumVersion')

if ([string]::IsNullOrWhiteSpace($pluginVersion)) { Fail 'Could not read plugin version from plugin.json' }
if ([string]::IsNullOrWhiteSpace($pluginMinVersion)) { Fail 'Could not read minHostVersion/minimumVersion from plugin.json' }
if ([string]::IsNullOrWhiteSpace($manifestVersion)) { Fail 'Could not read version from manifest.json' }
if ([string]::IsNullOrWhiteSpace($manifestMinVersion)) { Fail 'Could not read minHostVersion/minimumVersion from manifest.json' }

if ($manifestVersion -ne $pluginVersion) {
    Fail "manifest.json version ($manifestVersion) != plugin.json version ($pluginVersion)"
}
if ($manifestMinVersion -ne $pluginMinVersion) {
    Fail "manifest.json host version ($manifestMinVersion) != plugin.json host version ($pluginMinVersion)"
}

$readmeMatch = Select-String -Path 'README.md' -Pattern 'version-([0-9]+\.[0-9]+\.[0-9]+)' -List | Select-Object -First 1
if (-not $readmeMatch) { Fail 'Could not find version badge in README.md' }
$readmeVersion = $readmeMatch.Matches[0].Groups[1].Value
if ($readmeVersion -ne $pluginVersion) {
    Fail "README badge version ($readmeVersion) != plugin.json version ($pluginVersion)"
}

$manifestDoc = Get-Content 'docs/PLUGIN_MANIFEST.md' -Raw
$currentManifestBlock = [regex]::Match(
    $manifestDoc,
    '## Current Manifest\s*```json\s*(?<json>.*?)\s*```',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
if (-not $currentManifestBlock.Success) {
    Fail 'Could not find Current Manifest JSON block in docs/PLUGIN_MANIFEST.md'
}

foreach ($match in [regex]::Matches($currentManifestBlock.Groups['json'].Value, '"version":\s*"([0-9]+\.[0-9]+\.[0-9]+)"')) {
    $docVersion = $match.Groups[1].Value
    if ($docVersion -ne $pluginVersion) {
        Fail "docs/PLUGIN_MANIFEST.md Current Manifest version ($docVersion) does not match plugin.json ($pluginVersion)"
    }
}

$minMentions = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem 'docs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]archive[\\/]' -and $_.Extension -in '.md','.markdown','.mdown','.json','.yml','.yaml','.txt' } |
    ForEach-Object {
        $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) { $content = '' }
        foreach ($match in [regex]::Matches($content, '"(?:minHostVersion|minimumVersion)":\s*"([^"\r\n]+)"')) {
            $minMentions.Add($match.Groups[1].Value.Trim()) | Out-Null
        }
    }
foreach ($mv in $minMentions) {
    if ($mv -ne $pluginMinVersion) {
        Fail "docs host-version mention ($mv) != plugin.json minHostVersion ($pluginMinVersion)"
    }
}

$legacy = Select-String -Path @('docs','wiki-content') -Pattern '4\.0\.0\.0' -SimpleMatch -ErrorAction SilentlyContinue
if ($legacy) {
    Fail 'Found legacy minimum version 4.0.0.0 in docs/wiki'
}

$badPaths = @('/plugins/Brainarr', 'C:\\ProgramData\\Lidarr\\plugins\\Brainarr', '/config/plugins/Brainarr')
$searchFiles = @(Get-Item 'README.md') + (Get-ChildItem 'docs' -Recurse -File) + (Get-ChildItem 'wiki-content' -Recurse -File)
foreach ($pattern in $badPaths) {
    $hits = $searchFiles | Where-Object { -not $_.PSIsContainer } | ForEach-Object {
        Select-String -Path $_.FullName -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue
    } | Where-Object { $_ }
    if ($hits) {
        Fail "Found deprecated path pattern: $pattern"
    }
}

$compatFiles = @('README.md','docs/PROVIDER_GUIDE.md','docs/DEPLOYMENT.md','docs/USER_SETUP_GUIDE.md','wiki-content/Installation.md','wiki-content/Home.md')
$compatPattern = "Requires Lidarr\s*\**$pluginMinVersion\+\**\s*on the\s*\**plugins/nightly\**\s*branch"
foreach ($file in $compatFiles) {
    if (-not (Test-Path $file)) { Fail "Missing expected file $file" }
    if (-not (Select-String -Path $file -Pattern $compatPattern -Quiet)) {
        Fail "Missing compatibility notice in $file"
    }
}

function Normalize-Matrix {
    param([string]$Value)
    if ($null -eq $Value) { return '' }
    $normalized = ($Value -replace "`r", '').Split("`n") | ForEach-Object { $_.TrimEnd() }
    return ($normalized -join "`n").Trim()
}
function Get-ProviderMatrix {
    param([string]$Path)
    if (-not (Test-Path $Path)) { Fail "Missing file $Path" }
    $content = Get-Content $Path -Raw
    $match = [regex]::Match($content, '<!-- PROVIDER_MATRIX_START -->\s*(?<body>.*?)\s*<!-- PROVIDER_MATRIX_END -->', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $match.Success) { Fail "Missing provider matrix block in $Path" }
    return ($match.Groups['body'].Value.Trim())
}

$matrixDocs = Normalize-Matrix (Get-ProviderMatrix 'docs/PROVIDER_MATRIX.md')
$matrixReadme = Normalize-Matrix (Get-ProviderMatrix 'README.md')
$matrixWiki = Normalize-Matrix (Get-ProviderMatrix 'wiki-content/Home.md')

if ($matrixDocs -ne $matrixReadme) {
    Fail 'Provider matrix mismatch between docs/PROVIDER_MATRIX.md and README.md'
}
if ($matrixDocs -ne $matrixWiki) {
    Fail 'Provider matrix mismatch between docs/PROVIDER_MATRIX.md and wiki-content/Home.md'
}

$expectedReleaseLine = "Latest release: **v$pluginVersion**"
foreach ($target in @('README.md','wiki-content/Home.md')) {
    if (-not (Select-String -Path $target -Pattern ([regex]::Escape($expectedReleaseLine)) -Quiet)) {
        Fail "Missing latest release line in $target"
    }
}

if (-not (Select-String -Path 'docs/PROVIDER_MATRIX.md' -Pattern "Brainarr Provider Matrix \(v$pluginVersion\)" -Quiet)) {
    Fail "docs/PROVIDER_MATRIX.md header not updated for v$pluginVersion"
}

# ---------------------------------------------------------------------------
# Wiki link integrity
#
# Two failure modes that shipped unnoticed because nothing checked them, and both
# are invisible in the repo — they only break once the wiki is published:
#
#   1. A `HelpLink` in the settings UI ("More info") pointing at a wiki page or
#      anchor that does not exist. GitHub silently serves the top of the page (or
#      an empty page), so the user is dropped somewhere unrelated to the field
#      they clicked from. These links ship compiled into the plugin DLL, so a bad
#      one can only be fixed by editing the wiki — which is exactly why the wiki
#      side has to be verified here.
#   2. A relative `](../docs/...)` link inside wiki-content. The wiki is served
#      from a different path than the repo, so `../` resolves to a 404. Wiki
#      pages must use absolute https://github.com/... URLs.
# ---------------------------------------------------------------------------

function Get-MarkdownAnchors {
    param([Parameter(Mandatory = $true)][string]$Path)

    $anchors = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^#{1,6}\s+(.*)$') {
            # Mirror GitHub's slug rules: lowercase, drop anything that is not a
            # word character/space/hyphen, then spaces to hyphens.
            $slug = $Matches[1].Trim().ToLowerInvariant()
            $slug = [regex]::Replace($slug, '[^\w\s-]', '')
            $slug = [regex]::Replace($slug, '\s+', '-')
            [void]$anchors.Add($slug)
        }
    }
    return $anchors
}

$anchorCache = @{}
$linkErrors = New-Object System.Collections.Generic.List[string]
$wikiLinkPattern = '^https://github\.com/RicherTunes/Brainarr/wiki/([^#]+)(?:#(.+))?$'

foreach ($settingsFile in Get-ChildItem -Path 'Brainarr.Plugin' -Filter '*.cs' -File) {
    $content = Get-Content -LiteralPath $settingsFile.FullName -Raw
    foreach ($match in [regex]::Matches($content, 'HelpLink\s*=\s*"([^"]+)"')) {
        $link = $match.Groups[1].Value

        if ($link -notmatch '^https?://') {
            $linkErrors.Add("$($settingsFile.Name): HelpLink is not a URL: '$link'")
            continue
        }
        if ($link -notmatch $wikiLinkPattern) { continue }  # external link, not ours to verify

        $page = $Matches[1]
        $anchor = $Matches[2]
        $pagePath = Join-Path 'wiki-content' "$page.md"

        if (-not (Test-Path -LiteralPath $pagePath)) {
            $linkErrors.Add("$($settingsFile.Name): HelpLink targets missing wiki page '$page' ($link)")
            continue
        }
        if ([string]::IsNullOrEmpty($anchor)) { continue }

        if (-not $anchorCache.ContainsKey($pagePath)) {
            $anchorCache[$pagePath] = Get-MarkdownAnchors -Path $pagePath
        }
        if (-not $anchorCache[$pagePath].Contains($anchor)) {
            $linkErrors.Add("$($settingsFile.Name): HelpLink anchor '#$anchor' not found in $pagePath ($link)")
        }
    }
}

foreach ($wikiFile in Get-ChildItem -Path 'wiki-content' -Filter '*.md' -File) {
    $content = Get-Content -LiteralPath $wikiFile.FullName -Raw
    foreach ($match in [regex]::Matches($content, '\]\((\.\.?/[^)]+)\)')) {
        $linkErrors.Add("wiki-content/$($wikiFile.Name): relative link '$($match.Groups[1].Value)' does not resolve on the published wiki - use an absolute https://github.com/... URL")
    }
}

if ($linkErrors.Count -gt 0) {
    foreach ($linkError in $linkErrors) { Write-Host "  $linkError" -ForegroundColor Red }
    Fail "Wiki link integrity check failed ($($linkErrors.Count) problem(s))"
}

Ok "Docs consistency checks passed (version=$pluginVersion, minHostVersion=$pluginMinVersion)"
