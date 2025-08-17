# 🚨 URGENT: CI is Broken - Manual Fix Required

## The Problem
The CI builds are failing because the workflow doesn't build Lidarr from source first. GitHub App restrictions prevent automatic workflow updates.

## The Solution (5 minutes)

### Option 1: Quick Copy (Recommended)
```bash
# Copy the complete fixed workflow
cp workflow-templates/ci-updated.yml .github/workflows/ci.yml

# Commit and push
git add .github/workflows/ci.yml
git commit -m "fix: add Lidarr build steps to CI workflow"
git push origin terragon/review-build-fix-tech-debt
```

### Option 2: GitHub Web Interface
1. Go to: https://github.com/RicherTunes/Brainarr/edit/terragon/review-build-fix-tech-debt/.github/workflows/ci.yml
2. Copy contents from `workflow-templates/ci-updated.yml`
3. Paste into the editor
4. Commit directly to the branch

## What the Fix Does
- ✅ Adds Node.js setup for Yarn
- ✅ Builds Lidarr from source before building plugin
- ✅ Sets LIDARR_PATH environment variable
- ✅ Adds build caching for faster CI
- ✅ Updates all 3 CI jobs (test, build-plugin, security-scan)

## Verification
After applying the fix, the CI should:
1. ✅ Build Lidarr successfully from submodule
2. ✅ Build the plugin with proper Lidarr references
3. ✅ Pass all tests
4. ✅ Complete security scan

The failing errors like `CS0234: The type or namespace name 'Parser' does not exist` will be resolved because Lidarr assemblies will be available.

---
**This fix is ready to apply immediately. All the tech debt has been resolved except this final workflow update.**