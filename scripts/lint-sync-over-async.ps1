#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Lint for sync-over-async patterns in product code.

.DESCRIPTION
    Scans src/**/*.cs for .GetAwaiter().GetResult() (error) and
    .Result/.Wait() (warn-only) patterns, validating findings against
    a JSON allowlist.  Two CI modes:

      ci  — Strict: fails on ANY non-allowlisted match (main branch)
      pr  — Diff-aware: fails only on NEW matches introduced in this PR
    local — Same as ci but with verbose output (default)

    POLICY: Only Category A (host-init — Lidarr forces synchronous entry
    points) may be allowlisted.  Category B (avoidable) must be converted
    to async/await.  Category C (test-only) is out of scope (tests/ not
    scanned).

.PARAMETER Mode
    ci, pr, or local.  Default: local.

.PARAMETER AllowlistPath
    Path to sync-over-async-allowlist.json.
    Default: .github/sync-over-async-allowlist.json (relative to repo root).

.PARAMETER SrcPath
    Root directory to scan.  Default: src/ (relative to repo root).

.PARAMETER BaseBranch
    Base branch for diff-aware PR mode.  Default: origin/main.

.EXAMPLE
    # Local check (verbose)
    ./scripts/lint-sync-over-async.ps1

    # CI on main branch (strict)
    ./scripts/lint-sync-over-async.ps1 -Mode ci

    # CI on pull request (diff-aware)
    ./scripts/lint-sync-over-async.ps1 -Mode pr -BaseBranch origin/main
#>
[CmdletBinding()]
param(
    [ValidateSet('ci', 'pr', 'local')]
    [string]$Mode = 'local',

    [string]$AllowlistPath,
    [string]$SrcPath,
    [string]$BaseBranch = 'origin/main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve repo root ──────────────────────────────────────────────
$RepoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $RepoRoot) { $RepoRoot = (Get-Location).Path }
$RepoRoot = $RepoRoot.Replace('\', '/').TrimEnd('/')

# ── Defaults ───────────────────────────────────────────────────────
if (-not $AllowlistPath) { $AllowlistPath = "$RepoRoot/.github/sync-over-async-allowlist.json" }
if (-not $SrcPath)       { $SrcPath       = "$RepoRoot/src" }

# ── Load allowlist ─────────────────────────────────────────────────
$allowlist = @()
if (Test-Path $AllowlistPath) {
    $json = Get-Content $AllowlistPath -Raw | ConvertFrom-Json
    if ($json.entries) { $allowlist = @($json.entries) }
    Write-Host "Loaded $($allowlist.Count) allowlist entries from $(Split-Path $AllowlistPath -Leaf)"
}
else {
    Write-Host "No allowlist found at $AllowlistPath (all matches = violations)"
}

# ── Scan for pattern ───────────────────────────────────────────────
$Pattern = '\.GetAwaiter\(\)\.GetResult\(\)'

if (-not (Test-Path $SrcPath)) {
    Write-Host "[SKIP] Source path not found: $SrcPath"
    exit 0
}

$findings = @()
Get-ChildItem -Path $SrcPath -Filter '*.cs' -Recurse | ForEach-Object {
    $fullPath = $_.FullName.Replace('\', '/')
    $relPath  = $fullPath
    if ($fullPath.StartsWith("$RepoRoot/")) {
        $relPath = $fullPath.Substring($RepoRoot.Length + 1)
    }

    $lineNum = 0
    foreach ($line in (Get-Content $_.FullName)) {
        $lineNum++
        if ($line -match $Pattern) {
            $findings += [PSCustomObject]@{
                File    = $relPath
                Line    = $lineNum
                Content = $line.Trim()
            }
        }
    }
}

Write-Host "Found $($findings.Count) sync-over-async occurrence(s) in src/"

# ── Filter against allowlist ───────────────────────────────────────
# Line-number tolerance: allowlist line may drift ±10 lines from recorded value.
$LineTolerance = 10

$violations = @()
foreach ($f in $findings) {
    $allowed = $false
    foreach ($entry in $allowlist) {
        $entryFile = ($entry.file).Replace('\', '/')

        # Match: exact path or trailing match (handles repo-root prefix differences)
        $pathMatch = ($f.File -eq $entryFile) -or
                     ($f.File.EndsWith("/$entryFile")) -or
                     ($f.File.EndsWith($entryFile))

        if ($pathMatch -and [Math]::Abs($f.Line - $entry.line) -le $LineTolerance) {
            $allowed = $true
            if ($Mode -eq 'local') {
                Write-Host "  [ALLOW] $($f.File):$($f.Line) — cat=$($entry.category): $($entry.reason)" -ForegroundColor DarkGray
            }
            break
        }
    }
    if (-not $allowed) {
        $violations += $f
    }
}

# ── PR diff-aware filtering ───────────────────────────────────────
if ($Mode -eq 'pr' -and $violations.Count -gt 0) {
    Write-Host "PR mode: filtering to newly-added violations only (base: $BaseBranch)"

    $diffNewLines = @{}
    try {
        $diff = git diff "$BaseBranch...HEAD" -- $SrcPath 2>&1
        $currentFile = $null
        $newLineNum  = 0

        foreach ($dline in $diff) {
            if ($dline -match '^diff --git a/.+ b/(.+)$') {
                $currentFile = $Matches[1]
            }
            elseif ($dline -match '^@@ -\d+(?:,\d+)? \+(\d+)') {
                $newLineNum = [int]$Matches[1] - 1
            }
            elseif ($null -ne $currentFile) {
                if ($dline.StartsWith('+') -and -not $dline.StartsWith('+++')) {
                    $newLineNum++
                    if ($dline -match $Pattern) {
                        $key = "$currentFile`:$newLineNum"
                        $diffNewLines[$key] = $true
                    }
                }
                elseif ($dline.StartsWith('-') -and -not $dline.StartsWith('---')) {
                    # Deleted line: new-file counter does not advance
                }
                else {
                    $newLineNum++
                }
            }
        }
    }
    catch {
        Write-Host "::warning::Could not compute diff against $BaseBranch — falling back to strict mode"
        $diffNewLines = $null
    }

    if ($null -ne $diffNewLines) {
        $before     = $violations.Count
        $violations = @($violations | Where-Object {
            $key = "$($_.File):$($_.Line)"
            $diffNewLines.ContainsKey($key)
        })
        Write-Host "Diff filter: $before total -> $($violations.Count) new in this PR"
    }
}

# ── Report ─────────────────────────────────────────────────────────
if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "=== SYNC-OVER-ASYNC VIOLATIONS ===" -ForegroundColor Red

    foreach ($v in $violations) {
        $loc = "$($v.File):$($v.Line)"
        Write-Host "  $loc : $($v.Content)" -ForegroundColor Red
        if ($Mode -ne 'local') {
            # GitHub Actions annotation
            Write-Host "::error file=$($v.File),line=$($v.Line)::Sync-over-async: $($v.Content)"
        }
    }

    Write-Host ""
    Write-Host "To fix: convert to async/await."
    Write-Host "If Category A (host forces sync entry-point), add to allowlist:"
    Write-Host "  $AllowlistPath"
    $exitCode = 1
}
else {
    $exitCode = 0
}

# ── Warn-only scan: .Result / .Wait() ────────────────────────────
# These patterns MAY indicate sync-over-async but also have legitimate
# uses (.Result on ValueTask, .Result on non-Task types, .Wait(timeout)).
# Warn only - never fail CI.
$WarnPatterns = @(
    @{ Regex = '\.Result\b'; Label = '.Result' },
    @{ Regex = '\.Wait\(\)';  Label = '.Wait()' }
)

$totalWarnings = 0
foreach ($wp in $WarnPatterns) {
    $warnFindings = @()
    Get-ChildItem -Path $SrcPath -Filter '*.cs' -Recurse | ForEach-Object {
        $fullPath = $_.FullName.Replace('\', '/')
        $relPath  = $fullPath
        if ($fullPath.StartsWith("$RepoRoot/")) {
            $relPath = $fullPath.Substring($RepoRoot.Length + 1)
        }

        $lineNum = 0
        foreach ($line in (Get-Content $_.FullName)) {
            $lineNum++
            if ($line -match $wp.Regex) {
                # Skip comments and string literals (rough heuristic)
                $trimmed = $line.TrimStart()
                if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('///')) { continue }
                $warnFindings += [PSCustomObject]@{
                    File    = $relPath
                    Line    = $lineNum
                    Content = $line.Trim()
                }
            }
        }
    }

    if ($warnFindings.Count -gt 0) {
        Write-Host ""
        Write-Host "=== WARN: $($wp.Label) occurrences ($($warnFindings.Count)) ===" -ForegroundColor Yellow
        foreach ($w in $warnFindings) {
            $loc = "$($w.File):$($w.Line)"
            Write-Host "  $loc : $($w.Content)" -ForegroundColor Yellow
            if ($Mode -ne 'local') {
                Write-Host "::warning file=$($w.File),line=$($w.Line)::Sync-over-async (warn): $($w.Content)"
            }
        }
        $totalWarnings += $warnFindings.Count
    }
}

if ($totalWarnings -gt 0) {
    Write-Host ""
    Write-Host "[WARN] $totalWarnings .Result/.Wait() occurrence(s) found (non-blocking)" -ForegroundColor Yellow
}

if ($exitCode -eq 0) {
    Write-Host ""
    Write-Host "[OK] No sync-over-async violations" -ForegroundColor Green
}
exit $exitCode
