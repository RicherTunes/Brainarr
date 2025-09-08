# 🚀 Automated Release Process

This document explains how to create releases for the Brainarr plugin using our automated release system.

## 🎯 Quick Start

### Method 1: Simple Tag Release (Recommended)

Just create a tag - everything else is automated:

```bash
# Create and push a release tag
./.github/scripts/tag-release.sh 1.2.0

# That's it! The automation handles the rest:
# ✅ Updates version numbers
# ✅ Builds and tests plugin
# ✅ Generates release notes
# ✅ Creates GitHub release
```

### Method 2: Full Interactive Release

For more control over the release process:

```bash
# Interactive release with version bump choices
./.github/scripts/quick-release.sh

# Options:
# - patch (1.0.0 → 1.0.1) for bug fixes
# - minor (1.0.0 → 1.1.0) for new features
# - major (1.0.0 → 2.0.0) for breaking changes
# - custom version (e.g., 1.2.0-beta.1)
# - auto-detect from commit messages
```

### Method 3: Manual GitHub Actions

For advanced scenarios:

```bash
# Create a GitHub release manually
gh workflow run release.yml -f version=v1.2.0 -f draft=false
```

## 📋 Supported Version Formats

### Stable Releases

- `1.0.0` - Major release
- `1.1.0` - Minor release
- `1.0.1` - Patch release

### Pre-releases

- `1.2.0-alpha.1` - Alpha version
- `1.2.0-beta.1` - Beta version
- `1.2.0-rc.1` - Release candidate

## 🤖 What Happens During Release

When you trigger a release (by tag or workflow), the automation:

### 1. 🏷️ Version Processing

- Extracts version from tag/input
- Determines if it's a prerelease
- Finds previous version for changelog

### 2. 🔄 File Updates

- Updates `plugin.json` with new version
- Updates `.csproj` files with assembly versions
- Updates README.md version badges

### 3. 🔨 Building

- Extracts Lidarr assemblies from Docker (plugins branch)
- Builds plugin with .NET
- Runs full test suite

### 4. 📦 Packaging

- Packages minimal plugin files:
  - `Lidarr.Plugin.Brainarr.dll`
  - `plugin.json`
- Creates ZIP package: `Brainarr-{version}.net6.0.zip` (e.g., `Brainarr-1.1.0.net6.0.zip`)
- Computes SHA256 hashes and updates `manifest.json` (Windows build script)

### 5. 📝 Release Notes

Auto-generates comprehensive release notes with:

- Changes extracted from CHANGELOG.md or git commits
- Installation instructions (both GitHub URL and manual)
- Supported AI provider list
- Verification instructions
- Download links and checksums

### 6. 🎁 GitHub Release

- Creates GitHub release with professional formatting
- Attaches plugin ZIP and checksum files
- Sets prerelease flag for alpha/beta/rc versions
- Links to full changelog comparison

## 📊 Release Types

### 🟢 Stable Release

```bash
./.github/scripts/tag-release.sh 1.2.0
```

- Full production release
- Available to all users
- Shows in Lidarr plugin browser
- Default installation option

### 🟡 Beta Release

```bash
./.github/scripts/tag-release.sh 1.2.0-beta.1
```

- Feature preview for testing
- Marked as prerelease
- Available for early adopters
- Helps catch issues before stable

### 🔴 Alpha Release

```bash
./.github/scripts/tag-release.sh 1.2.0-alpha.1
```

- Development builds
- Experimental features
- May have known issues
- Developer/tester audience

## 🔧 Troubleshooting

### Release Failed

```bash
# Check GitHub Actions logs
gh run list --limit 5
gh run view <run-id>

# Common issues:
# - Tests failing (fix tests first)
# - Version conflicts (check plugin.json)
# - Build errors (verify dependencies)
```

### Version Rollback

```bash
# If you need to undo a version bump:
git reset --hard HEAD~1  # Undo commit
git tag -d v1.2.0        # Delete local tag
git push origin :v1.2.0  # Delete remote tag
```

### Manual Version Update

```bash
# Use the version bump script directly
pwsh .github/scripts/bump-version.ps1 -Version "1.2.0"
```

## 🎯 Best Practices

### Semantic Versioning

- **patch** (1.0.1): Bug fixes, documentation updates
- **minor** (1.1.0): New features, provider additions
- **major** (2.0.0): Breaking changes, architecture updates

### Pre-release Strategy

1. **Alpha**: Internal testing, major changes
2. **Beta**: Community testing, feature-complete
3. **RC**: Final testing, production-ready
4. **Stable**: Public release

### Changelog Maintenance

Keep `CHANGELOG.md` updated with:

```markdown
## [Unreleased]

### Added
- New AI provider support for XYZ

### Fixed
- Connection timeout issues with Ollama

### Changed
- Improved error handling for cloud providers
```

### Commit Message Format

Use conventional commits for auto-detection:

```bash
git commit -m "feat: add new AI provider XYZ"     # → minor
git commit -m "fix: resolve timeout issue"       # → patch
git commit -m "feat!: breaking API changes"      # → major
```

## 📈 Release Metrics

After release, monitor:

- **Download counts** (GitHub releases)
- **Installation success** (user feedback)
- **Error reports** (GitHub issues)
- **Performance** (CI/CD build times)

## 🔗 Integration

### Lidarr Plugin Browser

Stable releases automatically appear in:

- Lidarr → Settings → Plugins → Browse
- Search for "Brainarr"
- One-click install with GitHub URL

### GitHub Marketplace

Consider submitting to:

- GitHub Topics: `lidarr`, `plugin`, `ai`, `music`
- Plugin directories and awesome lists
- Community forums and documentation

---

*This automated release system saves time, reduces errors, and ensures consistent, professional releases for the Brainarr plugin. 🎉*
