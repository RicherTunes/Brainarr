#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Automated version bumping for Brainarr plugin
.DESCRIPTION
    Bumps version numbers across all relevant files and updates changelog
.PARAMETER BumpType
    Type of version bump: major, minor, patch, or auto (detect from commits)
.PARAMETER Version
    Explicit version to set (e.g., "1.2.0")
#>

param(
    [Parameter()]
    [ValidateSet("major", "minor", "patch", "auto")]
    [string]$BumpType = "auto",
    
    [Parameter()]
    [string]$Version = ""
)

# Set error handling
$ErrorActionPreference = "Stop"

function Get-CurrentVersion {
    $pluginJson = Get-Content "plugin.json" | ConvertFrom-Json
    return [System.Version]$pluginJson.version
}

function Set-Version {
    param([string]$NewVersion)
    
    Write-Host "🔄 Updating version to $NewVersion in all files..." -ForegroundColor Cyan
    
    # Update plugin.json
    $pluginJson = Get-Content "plugin.json" | ConvertFrom-Json
    $pluginJson.version = $NewVersion
    $pluginJson | ConvertTo-Json -Depth 10 | Set-Content "plugin.json"
    Write-Host "✅ Updated plugin.json" -ForegroundColor Green
    
    # Update .csproj files (if they have version properties)
    Get-ChildItem -Recurse -Name "*.csproj" | ForEach-Object {
        $content = Get-Content $_
        $updated = $false
        
        $content = $content | ForEach-Object {
            if ($_ -match '<AssemblyVersion>.*</AssemblyVersion>') {
                $updated = $true
                "    <AssemblyVersion>$NewVersion.0</AssemblyVersion>"
            }
            elseif ($_ -match '<FileVersion>.*</FileVersion>') {
                $updated = $true
                "    <FileVersion>$NewVersion.0</FileVersion>"
            }
            elseif ($_ -match '<AssemblyInformationalVersion>.*</AssemblyInformationalVersion>') {
                $updated = $true
                "    <AssemblyInformationalVersion>$NewVersion</AssemblyInformationalVersion>"
            }
            else {
                $_
            }
        }
        
        if ($updated) {
            $content | Set-Content $_
            Write-Host "✅ Updated $_" -ForegroundColor Green
        }
    }
    
    # Update README.md version badges
    $readme = Get-Content "README.md" -Raw
    $readme = $readme -replace '\[!\[Version\]\(https://img\.shields\.io/badge/version-[^-]+-brightgreen\)\]', "[![Version](https://img.shields.io/badge/version-$NewVersion-brightgreen)]"
    $readme | Set-Content "README.md"
    Write-Host "✅ Updated README.md version badge" -ForegroundColor Green
}

function Detect-BumpType {
    Write-Host "🔍 Analyzing recent commits to determine bump type..." -ForegroundColor Cyan
    
    # Get commits since last tag
    $lastTag = git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0) {
        $commits = git log --oneline "$lastTag..HEAD"
    } else {
        $commits = git log --oneline HEAD~10..HEAD
    }
    
    $hasMajor = $commits | Where-Object { $_ -match "^[a-f0-9]+ .*(BREAKING CHANGE|!:|major:)" }
    $hasMinor = $commits | Where-Object { $_ -match "^[a-f0-9]+ .*(feat\(|feat:|minor:)" }
    $hasPatch = $commits | Where-Object { $_ -match "^[a-f0-9]+ .*(fix\(|fix:|patch:)" }
    
    if ($hasMajor) {
        Write-Host "🔴 Detected BREAKING CHANGE → major bump" -ForegroundColor Red
        return "major"
    }
    elseif ($hasMinor) {
        Write-Host "🟡 Detected new features → minor bump" -ForegroundColor Yellow
        return "minor"
    }
    elseif ($hasPatch) {
        Write-Host "🟢 Detected bug fixes → patch bump" -ForegroundColor Green
        return "patch"
    }
    else {
        Write-Host "🔵 No significant changes detected → patch bump (default)" -ForegroundColor Blue
        return "patch"
    }
}

function Bump-Version {
    param([string]$BumpType)
    
    $current = Get-CurrentVersion
    Write-Host "📋 Current version: $current" -ForegroundColor White
    
    switch ($BumpType) {
        "major" { 
            $new = [System.Version]::new($current.Major + 1, 0, 0)
        }
        "minor" { 
            $new = [System.Version]::new($current.Major, $current.Minor + 1, 0)
        }
        "patch" { 
            $new = [System.Version]::new($current.Major, $current.Minor, $current.Build + 1)
        }
    }
    
    return $new.ToString()
}

function Update-Changelog {
    param([string]$NewVersion)
    
    Write-Host "📝 Updating CHANGELOG.md..." -ForegroundColor Cyan
    
    $changelogPath = "CHANGELOG.md"
    if (-not (Test-Path $changelogPath)) {
        Write-Warning "CHANGELOG.md not found, creating basic structure"
        @"
# Changelog

All notable changes to the Brainarr plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [$NewVersion] - $(Get-Date -Format 'yyyy-MM-dd')

### Added
- Version $NewVersion release

"@ | Set-Content $changelogPath
        return
    }
    
    $content = Get-Content $changelogPath
    $unreleasedIndex = $content.IndexOf("## [Unreleased]")
    
    if ($unreleasedIndex -ge 0) {
        # Insert new version section after unreleased
        $newSection = @(
            "",
            "## [$NewVersion] - $(Get-Date -Format 'yyyy-MM-dd')",
            ""
        )
        
        $newContent = @()
        $newContent += $content[0..($unreleasedIndex + 1)]
        $newContent += $newSection
        $newContent += $content[($unreleasedIndex + 2)..($content.Length - 1)]
        
        $newContent | Set-Content $changelogPath
        Write-Host "✅ Updated CHANGELOG.md with version $NewVersion" -ForegroundColor Green
    }
}

# Main execution
try {
    Write-Host "🧠 Brainarr Version Bump Script" -ForegroundColor Magenta
    Write-Host "================================" -ForegroundColor Magenta
    
    if ($Version) {
        Write-Host "📌 Using explicit version: $Version" -ForegroundColor Cyan
        Set-Version $Version
        Update-Changelog $Version
        Write-Host "🎉 Version bumped to $Version" -ForegroundColor Green
        exit 0
    }
    
    if ($BumpType -eq "auto") {
        $BumpType = Detect-BumpType
    }
    
    $newVersion = Bump-Version $BumpType
    Set-Version $newVersion
    Update-Changelog $newVersion
    
    Write-Host "🎉 Version bumped from $(Get-CurrentVersion) to $newVersion ($BumpType)" -ForegroundColor Green
    Write-Host "Next steps:" -ForegroundColor White
    Write-Host "  1. Review changes: git diff" -ForegroundColor Gray
    Write-Host "  2. Commit: git add . && git commit -m 'chore: bump version to $newVersion'" -ForegroundColor Gray
    Write-Host "  3. Tag: git tag v$newVersion" -ForegroundColor Gray
    Write-Host "  4. Push: git push origin main --tags" -ForegroundColor Gray
    
} catch {
    Write-Error "❌ Version bump failed: $_"
    exit 1
}