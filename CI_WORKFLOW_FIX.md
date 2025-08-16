# CI Workflow Fix - Final Step

## Current Status
✅ **PR #47 merged** - Lidarr submodule is now in the repository  
❌ **CI still failing** - Workflow doesn't fetch the submodule

## The Problem
The CI workflow has `submodules: false` (default), causing:
```
git fetch --no-recurse-submodules
```

## The Solution - Manual Update Required

### Edit `.github/workflows/ci.yml` on GitHub:

1. **Go to:** https://github.com/RicherTunes/Brainarr/edit/main/.github/workflows/ci.yml

2. **Find these 3 sections and add `with: submodules: true`:**

#### First occurrence (line ~22):
```yaml
# Change from:
- name: Checkout
  uses: actions/checkout@v4

# To:
- name: Checkout
  uses: actions/checkout@v4
  with:
    submodules: true
```

#### Second occurrence (line ~84):
```yaml
# Change from:
- name: Checkout
  uses: actions/checkout@v4

# To:
- name: Checkout
  uses: actions/checkout@v4
  with:
    submodules: true
```

#### Third occurrence (line ~133):
```yaml
# Change from:
- name: Checkout
  uses: actions/checkout@v4

# To:
- name: Checkout
  uses: actions/checkout@v4
  with:
    submodules: true
```

3. **Remove mock DLL creation steps:**
   - Delete lines 37-49 (first "Setup Lidarr Dependencies (Mock)" block)
   - Delete lines 91-99 (second mock block)
   - Delete lines 145-154 (third mock block)

4. **Commit with message:** 
   ```
   fix: Enable submodule fetching in CI workflow
   ```

## Why This Will Work

| Component | Status | Notes |
|-----------|--------|-------|
| Lidarr submodule | ✅ Exists | Added by PR #47 |
| .gitmodules | ✅ Tracked | Defines submodule location |
| .gitignore | ✅ Fixed | No longer blocks ext/ |
| CI workflow | ❌ Needs update | Must add `submodules: true` |

## Expected Result After Fix
1. CI fetches the repository
2. CI fetches the Lidarr submodule
3. Build finds all Lidarr projects
4. Compilation succeeds
5. Tests pass
6. ✅ Green build!

## Alternative: Use Fixed Workflow
Copy the entire content from `.github-workflows-fixed/ci.yml` in this repository.