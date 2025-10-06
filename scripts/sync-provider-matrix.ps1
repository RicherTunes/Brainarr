param()

$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    param([string]$current)
    return [System.IO.Path]::GetFullPath((Join-Path $current '..'))
}

function Parse-ProvidersYaml {
    param([string[]]$lines)

    $result = [ordered]@{ min_lidarr = ''; providers = New-Object System.Collections.Generic.List[object] }
    $current = $null

    foreach ($rawLine in $lines) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        if ($line -match '^min_lidarr:\s*"(.+)"$') {
            $result.min_lidarr = $matches[1]
            continue
        }

        if ($line -match '^\-\s+name:\s*"(.+)"$') {
            if ($current) { $result.providers.Add($current) }
            $current = [ordered]@{ name = $matches[1]; type = ''; status = ''; notes = '' }
            continue
        }

        if ($null -eq $current) { continue }

        if ($line -match '^type:\s*"(.+)"$') {
            $current.type = $matches[1]
            continue
        }

        if ($line -match '^status:\s*"(.+)"$') {
            $current.status = $matches[1]
            continue
        }

        if ($line -match '^notes:\s*"(.*)"$') {
            $current.notes = $matches[1]
            continue
        }
    }

    if ($current) { $result.providers.Add($current) }
    return $result
}

function Build-ProviderTable {
    param($data)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('| Provider | Type | Status | Notes |')
    $lines.Add('| --- | --- | --- | --- |')
    foreach ($provider in $data.providers) {
        $notes = if ([string]::IsNullOrWhiteSpace($provider.notes)) { '' } else { $provider.notes }
        $lines.Add("| $($provider.name) | $($provider.type) | $($provider.status) | $notes |")
    }

    return [string]::Join("`n", $lines)
}

function Normalize-LineEndings {
    param([string]$Value)
    return ($Value -replace "`r`n", "`n") -replace "`r", "`n"
}

function Update-DocumentSection {
    param(
        [string]$Path,
        [string]$Table
    )

    $originalRaw = Get-Content -Path $Path -Raw -Encoding UTF8
    $normalizedOriginal = Normalize-LineEndings -Value $originalRaw

    $pattern = '(?s)(<!-- PROVIDER_MATRIX_START -->)\s*(.*?)(\s*<!-- PROVIDER_MATRIX_END -->)'
    if ($normalizedOriginal -notmatch $pattern) {
        throw "Provider matrix markers not found in $Path"
    }

    $updated = [System.Text.RegularExpressions.Regex]::Replace(
        $normalizedOriginal,
        $pattern,
        { param($m)
            $start = ($m.Groups[1].Value).TrimEnd()
            $end = ($m.Groups[3].Value).TrimStart()
            return "$start`n$Table`n$end"
        },
        1
    )

    $updated = [System.Text.RegularExpressions.Regex]::Replace($updated, '(?m)(\n){3,}', "`n`n")

    if (-not $updated.EndsWith("`n")) {
        $updated += "`n"
    }

    if ($updated -eq $normalizedOriginal) {
        return
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $updated, $utf8NoBom)
    Write-Host "Updated $Path"
}

$scriptRoot = $PSScriptRoot
$repoRoot = Get-RepositoryRoot -current $scriptRoot
$yamlPath = Join-Path $repoRoot 'docs/providers.yaml'
if (-not (Test-Path $yamlPath)) {
    throw "providers.yaml not found at $yamlPath"
}

[string[]]$yamlLines = Get-Content -Path $yamlPath
$providerData = Parse-ProvidersYaml -lines $yamlLines
$table = Build-ProviderTable -data $providerData

$documents = @(
    (Join-Path $repoRoot 'README.md'),
    (Join-Path $repoRoot 'docs/PROVIDER_MATRIX.md'),
    (Join-Path $repoRoot 'wiki-content/Home.md')
)

foreach ($doc in $documents) {
    Update-DocumentSection -Path $doc -Table $table
}

Write-Host "Provider matrix synced."
