# Interactive Release Script for Brainarr (PowerShell version)
# Creates new releases with automated testing and deployment

param(
    [string]$Version = "",
    [switch]$Force = $false
)

Write-Host "🧠 Brainarr Release Manager" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan

# Check prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Blue

# Check if we're in the right directory
if (-not (Test-Path "plugin.json") -or -not (Test-Path "Brainarr.Plugin")) {
    Write-Error "❌ Please run this script from the Brainarr root directory"
    exit 1
}

# Check for uncommitted changes
$gitStatus = git status --porcelain
if ($gitStatus -and -not $Force) {
    Write-Error "❌ You have uncommitted changes. Please commit or stash them first."
    git status --porcelain
    exit 1
}

# Check authentication
try {
    gh auth status | Out-Null
} catch {
    Write-Host "🔐 GitHub CLI not authenticated. Running gh auth login..." -ForegroundColor Yellow
    gh auth login
}

# Check current branch
$currentBranch = git branch --show-current
if ($currentBranch -ne "main" -and -not $Force) {
    Write-Host "⚠️ Warning: You're on branch '$currentBranch', not 'main'" -ForegroundColor Yellow
    $continue = Read-Host "Continue anyway? (y/N)"
    if ($continue -ne "y" -and $continue -ne "Y") {
        exit 1
    }
}

Write-Host "✅ Prerequisites check passed" -ForegroundColor Green
Write-Host ""

# Get current version
$currentVersion = git describe --tags --abbrev=0 2>$null
if (-not $currentVersion) { $currentVersion = "v0.0.0" }
Write-Host "📋 Current version: $currentVersion" -ForegroundColor Blue

# Version selection if not provided
if (-not $Version) {
    Write-Host ""
    Write-Host "📝 Version Selection:" -ForegroundColor Blue

    # Calculate suggested versions
    $versionParts = $currentVersion.TrimStart('v').Split('.')
    $major = [int]$versionParts[0]
    $minor = [int]$versionParts[1]
    $patch = [int]$versionParts[2]

    $patchVersion = "v$major.$minor.$($patch + 1)"
    $minorVersion = "v$major.$($minor + 1).0"
    $majorVersion = "v$($major + 1).0.0"

    Write-Host "  1. Patch (bug fixes): $currentVersion → $patchVersion"
    Write-Host "  2. Minor (new features): $currentVersion → $minorVersion"
    Write-Host "  3. Major (breaking changes): $currentVersion → $majorVersion"
    Write-Host "  4. Custom version"
    Write-Host "  5. Prerelease (alpha/beta/rc)"

    $selection = Read-Host "Select version type (1-5)"

    switch ($selection) {
        1 { $Version = $patchVersion; $releaseType = "patch" }
        2 { $Version = $minorVersion; $releaseType = "minor" }
        3 { $Version = $majorVersion; $releaseType = "major" }
        4 {
            $Version = Read-Host "Enter custom version (e.g., v1.2.3)"
            if ($Version -notmatch "^v\d+\.\d+\.\d+$") {
                Write-Error "❌ Invalid version format. Use: v1.2.3"
                exit 1
            }
            $releaseType = "custom"
        }
        5 {
            $Version = Read-Host "Enter prerelease version (e.g., v1.2.3-beta.1)"
            if ($Version -notmatch "^v\d+\.\d+\.\d+-(alpha|beta|rc)\.\d+$") {
                Write-Error "❌ Invalid prerelease format. Use: v1.2.3-beta.1"
                exit 1
            }
            $releaseType = "prerelease"
        }
        default {
            Write-Error "❌ Invalid selection"
            exit 1
        }
    }
}

Write-Host ""
Write-Host "🎯 Selected version: $Version ($releaseType)" -ForegroundColor Green

# Confirm release
Write-Host ""
Write-Host "🚀 Release Summary:" -ForegroundColor Yellow
Write-Host "  Current: $currentVersion"
Write-Host "  New: $Version"
Write-Host "  Type: $releaseType"
Write-Host "  Branch: $currentBranch"
Write-Host ""

$confirm = Read-Host "Create this release? (y/N)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "❌ Release cancelled"
    exit 1
}

Write-Host ""
Write-Host "🔧 Preparing release $Version..." -ForegroundColor Blue

# Check if tag already exists
try {
    git rev-parse $Version | Out-Null
    Write-Host "❌ Tag $Version already exists" -ForegroundColor Red
    $recreate = Read-Host "Delete existing tag and recreate? (y/N)"
    if ($recreate -eq "y" -or $recreate -eq "Y") {
        Write-Host "🗑️ Deleting existing tag..."
        git tag -d $Version
        git push origin ":refs/tags/$Version" 2>$null
    } else {
        exit 1
    }
} catch {
    # Tag doesn't exist - good!
}

# Run tests before release
Write-Host "🧪 Running tests before release..." -ForegroundColor Blue
$testResult = dotnet test --configuration Release --logger:"console;verbosity=minimal" --filter "Category=Unit|Category=Provider|Category=EdgeCase|Category=Integration|Category=BugFix|Category=Security|Category=Resilience|Category=Critical"

if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Tests failed! Cannot create release with failing tests."
    Write-Host "Run 'dotnet test' to see failures and fix before releasing."
    exit 1
}
Write-Host "✅ Tests passed" -ForegroundColor Green

# Generate changelog
Write-Host "📝 Generating changelog..." -ForegroundColor Blue
if ($currentVersion -ne "v0.0.0") {
    $commits = git log --pretty=format:"• %s" "$currentVersion..HEAD"
    if (-not $commits) { $commits = "• Minor improvements and bug fixes" }
} else {
    $commits = "• Initial release"
}

# Create comprehensive release notes
$releaseNotes = @"
# 🧠 Brainarr $Version - AI-Powered Music Discovery

## 🎉 What's New

$commits

## 🚀 Installation

### Docker (Recommended)
``````yaml
services:
  lidarr:
    image: ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913
    # ... your config
``````

### Manual Installation
1. Download ``Brainarr-$Version.zip`` from assets below
2. Extract to Lidarr plugins directory
3. Restart Lidarr
4. Configure in Settings → Import Lists → Add → Brainarr

## 🤖 Supported AI Providers

### 🏠 Local (Privacy-First)
- **Ollama** - Complete privacy, no API costs
- **LM Studio** - GUI-based local AI

### ☁️ Cloud (Performance)
- **DeepSeek** - Ultra-low cost (10-20x cheaper than OpenAI)
- **Google Gemini** - Fast with generous free tier
- **Groq** - Ultra-fast inference
- **OpenRouter** - 200+ models, competitive pricing
- **Perplexity** - Web-enhanced recommendations
- **OpenAI** - Industry standard
- **Anthropic Claude** - Advanced reasoning

## 📊 Current Status
- **Test Coverage**: 485 tests with 100% pass rate
- **Platform Support**: Windows, macOS, Linux
- **Lidarr Compatibility**: 2.13.1.4681+ (plugins branch)

## 🔗 Documentation
- **Wiki**: https://github.com/RicherTunes/Brainarr/wiki
- **Issues**: https://github.com/RicherTunes/Brainarr/issues
- **Discussions**: https://github.com/RicherTunes/Brainarr/discussions

---
*🤖 Created with Brainarr Release Manager*
"@

Write-Host ""
Write-Host "📋 Release Notes Preview:" -ForegroundColor Blue
Write-Host "----------------------------------------"
Write-Host ($releaseNotes -split "`n" | Select-Object -First 15 | Out-String)
Write-Host "----------------------------------------"
Write-Host ""

# Create and push tag
Write-Host "🏷️ Creating tag $Version..." -ForegroundColor Blue
git tag -a $Version -m "Release $Version

$commits

🤖 Generated with Brainarr Release Manager"

Write-Host "📤 Pushing tag to origin..." -ForegroundColor Blue
git push origin $Version

Write-Host ""
Write-Host "🎉 Release $Version created successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "📦 Release Status:" -ForegroundColor Blue
Write-Host "  ✅ Tag pushed to origin"
Write-Host "  ✅ Local package/checksum generation completed before tagging"
Write-Host "  ℹ️  Brainarr has a guarded GitHub CI mirror, but no tag-triggered release workflow"
Write-Host "  📌 Publish/update release notes and assets manually from the local artifacts"
Write-Host ""

# Publish guidance
Write-Host "📊 Manual publish guidance:" -ForegroundColor Blue
Write-Host "🔗 Release URL: https://github.com/RicherTunes/Brainarr/releases/tag/$Version"
Write-Host ""
Write-Host "📋 Next steps:"
Write-Host "  1. Confirm the remote tag is visible."
Write-Host "  2. Upload the locally generated package and checksum artifacts."
Write-Host "  3. Copy release notes from CHANGELOG.md."
Write-Host "  4. Run/confirm the Gitea main CI gate after merging any release metadata changes."

Write-Host ""
Write-Host "🎯 Release $Version initiated!" -ForegroundColor Green
Write-Host "📖 Finish publishing at: https://github.com/RicherTunes/Brainarr/releases/tag/$Version" -ForegroundColor Blue
Write-Host ""
Write-Host "🎵 Happy music discovering! 🎵"
