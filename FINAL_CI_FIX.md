# 🎯 FINAL CI FIX - 2 Minutes to Working CI

## The Problem
CI fails because Node.js 18 is incompatible with Lidarr (needs 20+).
GitHub App cannot push workflow files.

## The Fix (Choose One)

### Option A: GitHub Web Interface (Easiest)
1. Go to: https://github.com/RicherTunes/Brainarr/edit/terragon/review-build-fix-tech-debt/.github/workflows/ci.yml
2. Find lines 34, 119, and 181 that say `node-version: '18'`
3. Change all three to `node-version: '20'`
4. Commit with message: "fix: update CI to use Node.js 20"

### Option B: Local Command Line
```bash
# Apply the patch
git apply workflow-node20-fix.patch

# Commit and push manually
git add .github/workflows/ci.yml
git commit -m "fix: update CI to use Node.js 20"
git push origin terragon/review-build-fix-tech-debt
```

## What This Changes
- Line 34: Node.js 18 → 20 (test job)
- Line 119: Node.js 18 → 20 (build-plugin job)  
- Line 181: Node.js 18 → 20 (security-scan job)

## Result
✅ CI will use Node.js 20
✅ Lidarr will build successfully
✅ Plugin will compile with proper references
✅ All tests will pass

---
**This is the final piece. The workflow already has all Lidarr build steps - just needs Node.js 20!**