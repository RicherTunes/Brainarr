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

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

try {
    $plugin = Get-Content 'plugin.json' -Raw | ConvertFrom-Json
    $manifest = Get-Content 'manifest.json' -Raw | ConvertFrom-Json
} catch {
    Fail 'Unable to parse plugin.json or manifest.json'
}

$pluginVersion = $plugin.version
$pluginMinVersion = $plugin.minimumVersion
$manifestVersion = $manifest.version
$manifestMinVersion = $manifest.minimumVersion

if ([string]::IsNullOrWhiteSpace($pluginVersion)) { Fail 'Could not read plugin version from plugin.json' }
if ([string]::IsNullOrWhiteSpace($pluginMinVersion)) { Fail 'Could not read minimumVersion from plugin.json' }
if ([string]::IsNullOrWhiteSpace($manifestVersion)) { Fail 'Could not read version from manifest.json' }
if ([string]::IsNullOrWhiteSpace($manifestMinVersion)) { Fail 'Could not read minimumVersion from manifest.json' }

if ($manifestVersion -ne $pluginVersion) {
    Fail "manifest.json version ($manifestVersion) != plugin.json version ($pluginVersion)"
}
if ($manifestMinVersion -ne $pluginMinVersion) {
    Fail "manifest.json minimumVersion ($manifestMinVersion) != plugin.json minimumVersion ($pluginMinVersion)"
}

$readmeMatch = Select-String -Path 'README.md' -Pattern 'version-([0-9]+\.[0-9]+\.[0-9]+)' -List | Select-Object -First 1
if (-not $readmeMatch) { Fail 'Could not find version badge in README.md' }
$readmeVersion = $readmeMatch.Matches[0].Groups[1].Value
if ($readmeVersion -ne $pluginVersion) {
    Fail "README badge version ($readmeVersion) != plugin.json version ($pluginVersion)"
}

$docVersions = [System.Collections.Generic.HashSet[string]]::new()
foreach ($match in [regex]::Matches((Get-Content 'docs/PLUGIN_MANIFEST.md' -Raw), '"version":\s*"([0-9]+\.[0-9]+\.[0-9]+)"')) {
    $docVersions.Add($match.Groups[1].Value) | Out-Null
}
foreach ($v in $docVersions) {
    if ($v -ne $pluginVersion) {
        Fail "docs/PLUGIN_MANIFEST.md version example ($v) does not match plugin.json ($pluginVersion)"
    }
}

$minMentions = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem 'docs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/]archive[\\/]' -and $_.Extension -in '.md','.markdown','.mdown','.json','.yml','.yaml','.txt' } |
    ForEach-Object {
        $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) { $content = '' }
        foreach ($match in [regex]::Matches($content, '"minimumVersion":\s*"([^"\r\n]+)"')) {
            $minMentions.Add($match.Groups[1].Value.Trim()) | Out-Null
        }
    }
foreach ($mv in $minMentions) {
    if ($mv -ne $pluginMinVersion) {
        Fail "docs minimumVersion mention ($mv) != plugin.json minimumVersion ($pluginMinVersion)"
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

Ok "Docs consistency checks passed (version=$pluginVersion, minimumVersion=$pluginMinVersion)"
