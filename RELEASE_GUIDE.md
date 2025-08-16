# 🚀 Brainarr Release Guide

This guide explains how to create releases for Brainarr, both manual and automated.

## 📋 Table of Contents

- [🏷️ Release Types](#️-release-types)
- [🔄 Automated Release Process](#-automated-release-process)
- [📝 Manual Release Process](#-manual-release-process)
- [🧪 Pre-Release Checklist](#-pre-release-checklist)
- [📦 Version Management](#-version-management)
- [🔍 Release Validation](#-release-validation)
- [📢 Post-Release Tasks](#-post-release-tasks)

---

## 🏷️ Release Types

### Version Numbering (Semantic Versioning)

| Type | Example | When to Use |
|------|---------|-------------|
| **Patch** | 1.0.0 → 1.0.1 | Bug fixes, security patches |
| **Minor** | 1.0.0 → 1.1.0 | New features, provider additions |
| **Major** | 1.0.0 → 2.0.0 | Breaking changes, major redesigns |

### Release Categories

- **🚀 Stable Release**: Production-ready, fully tested
- **🧪 Pre-Release**: Beta/RC versions for testing
- **🔥 Hotfix**: Critical bug fixes

---

## 🔄 Automated Release Process (Recommended)

### Method 1: GitHub Actions Workflow

1. **Navigate to GitHub Actions**
   ```
   GitHub Repository → Actions → Release Management → Run workflow
   ```

2. **Configure Release Parameters**
   ```yaml
   Version bump type: patch|minor|major
   Is pre-release: false (for stable) / true (for beta)
   Release notes: Optional custom notes
   ```

3. **Workflow Execution**
   - ✅ Calculates new version number
   - ✅ Updates `plugin.json`, `README.md`, and `CHANGELOG.md`
   - ✅ Commits version changes
   - ✅ Builds release package
   - ✅ Creates Git tag
   - ✅ Creates GitHub release with assets
   - ✅ Generates checksums and release notes

### Method 2: Version Bump Scripts

**Windows (PowerShell):**
```powershell
# Navigate to project root
cd path\to\brainarr

# Bump version (patch/minor/major)
.\scripts\bump-version.ps1 -BumpType patch

# Review changes
git diff

# Commit and push
git add .
git commit -m "chore: bump version to X.X.X"
git push origin main

# Then use GitHub Actions to create release
```

**Linux/macOS (Bash):**
```bash
# Navigate to project root
cd path/to/brainarr

# Bump version
./scripts/bump-version.sh patch

# Review changes
git diff

# Commit and push
git add .
git commit -m "chore: bump version to X.X.X"
git push origin main

# Then use GitHub Actions to create release
```

---

## 📝 Manual Release Process

### Step 1: Prepare Release

1. **Update Version Numbers**
   ```bash
   # Update plugin.json
   {
     "version": "1.1.0",  # <-- Update this
     ...
   }
   
   # Update README.md badges
   [![Version](https://img.shields.io/badge/version-1.1.0-brightgreen)]
   ```

2. **Update CHANGELOG.md**
   ```markdown
   ## [1.1.0] - 2025-01-20
   
   ### Added
   - New AI provider: AWS Bedrock
   - Cost monitoring dashboard
   
   ### Changed
   - Improved recommendation quality
   
   ### Fixed
   - Fixed cache invalidation bug
   ```

### Step 2: Build Release Package

```bash
# Restore and build
dotnet restore
dotnet build --configuration Release

# Create package directory
mkdir Brainarr-v1.1.0

# Copy plugin files
cp Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll Brainarr-v1.1.0/
cp plugin.json Brainarr-v1.1.0/
cp Brainarr.Plugin/bin/NLog.dll Brainarr-v1.1.0/
cp Brainarr.Plugin/bin/Newtonsoft.Json.dll Brainarr-v1.1.0/
cp Brainarr.Plugin/bin/FluentValidation.dll Brainarr-v1.1.0/

# Create installation instructions
cat > Brainarr-v1.1.0/INSTALL.txt << 'EOF'
Brainarr v1.1.0 Installation Instructions
========================================

1. Copy all files to your Lidarr plugins directory:
   - Windows: C:\ProgramData\Lidarr\plugins\Brainarr\
   - Linux: /var/lib/lidarr/plugins/Brainarr/
   - Docker: /config/plugins/Brainarr/

2. Restart Lidarr

3. Go to Settings → Import Lists → Add → Brainarr

For detailed setup: https://github.com/yourusername/brainarr/blob/main/QUICKSTART.md
EOF

# Create ZIP archive
zip -r Brainarr-v1.1.0.zip Brainarr-v1.1.0/

# Generate checksum
sha256sum Brainarr-v1.1.0.zip > checksums.txt
```

### Step 3: Create GitHub Release

```bash
# Create and push tag
git tag -a v1.1.0 -m "Release v1.1.0: New features and improvements"
git push origin v1.1.0

# Then create release manually on GitHub:
# 1. Go to Releases → Draft a new release
# 2. Choose tag: v1.1.0
# 3. Title: 🎉 Brainarr v1.1.0
# 4. Upload: Brainarr-v1.1.0.zip and checksums.txt
# 5. Add release notes from CHANGELOG.md
```

---

## 🧪 Pre-Release Checklist

### ✅ Code Quality

- [ ] All tests pass locally (`dotnet test`)
- [ ] No build warnings or errors
- [ ] Code formatting is correct (`dotnet format`)
- [ ] Security scan passes (if available)

### ✅ Functionality

- [ ] Plugin builds successfully
- [ ] All 9 AI providers work correctly
- [ ] Test with at least 2 different providers
- [ ] Configuration validation works
- [ ] Recommendations are generated successfully

### ✅ Documentation

- [ ] CHANGELOG.md updated with changes
- [ ] Version numbers updated in all files
- [ ] README.md reflects new features (if any)
- [ ] FAQ.md updated with new issues/solutions (if needed)

### ✅ Compatibility

- [ ] Tested with latest Lidarr version
- [ ] Tested with minimum supported Lidarr version (4.0.0)
- [ ] Tested on multiple platforms (Windows/Linux/Docker)

### ✅ Package Quality

- [ ] Release package contains all necessary files
- [ ] No sensitive information in package
- [ ] Installation instructions are clear
- [ ] Checksums are accurate

---

## 📦 Version Management

### Current Version Information

**Current Version**: Check `plugin.json`
```json
{
  "version": "1.0.0"
}
```

**Compatibility**: 
- Minimum Lidarr: 4.0.0.0
- Target .NET: 6.0

### Version Update Locations

When updating versions, modify these files:

1. **plugin.json** - Main version number
2. **README.md** - Version badge
3. **CHANGELOG.md** - Add new version entry
4. **docs/ROADMAP.md** - Update current status (if needed)

### Automated Version Updates

The automated release workflow updates all these files automatically when you use:
- GitHub Actions Release Management workflow
- Version bump scripts (`scripts/bump-version.*`)

---

## 🔍 Release Validation

### Post-Release Verification

1. **Download Test**
   ```bash
   # Download the release
   wget https://github.com/yourusername/brainarr/releases/download/v1.1.0/Brainarr-v1.1.0.zip
   
   # Verify checksum
   sha256sum -c checksums.txt
   ```

2. **Installation Test**
   ```bash
   # Extract and test install
   unzip Brainarr-v1.1.0.zip
   ls -la Brainarr-v1.1.0/
   
   # Should contain:
   # - Lidarr.Plugin.Brainarr.dll
   # - plugin.json
   # - Dependencies (.dll files)
   # - INSTALL.txt
   ```

3. **Functionality Test**
   - Install in test Lidarr instance
   - Verify plugin loads correctly
   - Test basic configuration
   - Generate test recommendations

### Release Quality Metrics

- **Package Size**: Should be < 10MB
- **Load Time**: Plugin should load in < 5 seconds
- **Memory Usage**: < 50MB during normal operation
- **Recommendation Speed**: < 30 seconds for 10 recommendations

---

## 📢 Post-Release Tasks

### Immediate Tasks (Within 24 hours)

1. **Monitor for Issues**
   - Watch GitHub Issues for bug reports
   - Monitor community discussions
   - Check CI/CD pipeline health

2. **Update Documentation**
   - Ensure all docs reflect new version
   - Update any version-specific instructions
   - Verify all links work correctly

3. **Community Notification**
   - Post in relevant forums/communities
   - Update project status in README
   - Respond to early user feedback

### Follow-up Tasks (Within 1 week)

1. **Usage Analytics**
   - Monitor download statistics
   - Track adoption rate
   - Identify popular features

2. **Feedback Integration**
   - Address critical issues quickly
   - Plan hotfixes if needed
   - Collect enhancement requests

3. **Next Release Planning**
   - Update roadmap based on feedback
   - Plan next version features
   - Update development priorities

---

## 🆘 Troubleshooting Releases

### Common Issues

**GitHub Actions Fails:**
- Check workflow syntax
- Verify secrets/tokens are configured
- Review build logs for specific errors

**Package Missing Files:**
- Verify build output directory
- Check `.csproj` file copy settings
- Ensure dependencies are included

**Version Conflicts:**
- Check all files are updated consistently
- Verify Git tags match version numbers
- Ensure no cached versions interfere

### Emergency Procedures

**Critical Bug in Release:**
1. Create hotfix branch
2. Fix the critical issue
3. Use automated release with patch version
4. Mark previous release as problematic (GitHub)

**Release Rollback:**
1. Create new release with previous version number + patch
2. Revert problematic changes
3. Communicate issue to users
4. Document lessons learned

---

## 📝 Release Checklist Template

Use this for each release:

```markdown
## Release Checklist for v1.X.X

### Pre-Release
- [ ] All tests pass
- [ ] Version numbers updated
- [ ] CHANGELOG.md updated
- [ ] Build package created
- [ ] Installation tested

### Release
- [ ] Git tag created
- [ ] GitHub release published
- [ ] Assets uploaded
- [ ] Release notes complete

### Post-Release
- [ ] Download verified
- [ ] Installation tested
- [ ] Basic functionality verified
- [ ] Community notified
- [ ] Issues monitored

### Issues Found
- [ ] Issue #1: [Description]
- [ ] Issue #2: [Description]

### Next Release Planning
- [ ] Feature 1: [Description]
- [ ] Enhancement 2: [Description]
```

---

## 🔗 Additional Resources

- **GitHub Releases**: [Repository Releases Page](https://github.com/yourusername/brainarr/releases)
- **CI/CD Workflows**: [.github/workflows/](.github/workflows/)
- **Version Scripts**: [scripts/](scripts/)
- **Changelog**: [CHANGELOG.md](CHANGELOG.md)
- **Roadmap**: [docs/ROADMAP.md](docs/ROADMAP.md)

---

*Last Updated: January 2025*  
*Next Review: After v1.1.0 release*