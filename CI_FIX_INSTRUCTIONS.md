# CI Build Fix Instructions

## Root Cause
The CI is failing because the GitHub Actions workflow is **not fetching the Lidarr submodule**. 

All checkout steps in `.github/workflows/ci.yml` have `submodules: false` (the default), but our project now requires the Lidarr submodule to build.

## Error Summary
```
error CS0234: The type or namespace name 'Parser' does not exist in the namespace 'NzbDrone.Core'
warning: The referenced project '../ext/Lidarr/src/NzbDrone.Core/Lidarr.Core.csproj' does not exist
```

## Quick Fix Required

### Option 1: Direct Web Edit (Recommended)
1. Go to: https://github.com/RicherTunes/Brainarr/blob/main/.github/workflows/ci.yml
2. Click "Edit this file" (pencil icon)
3. **Replace ALL** `uses: actions/checkout@v4` blocks with:
   ```yaml
   - name: Checkout
     uses: actions/checkout@v4
     with:
       submodules: true
   ```
4. **Remove ALL** mock DLL creation steps (lines 37-49, 79-87, 124-132)
5. **Change** dotnet matrix from `['6.0.x', '8.0.x']` to `['6.0.x']` (line 18)
6. Commit directly to main branch

### Option 2: Complete Replacement
Replace the entire `.github/workflows/ci.yml` content with the fixed version from `.github-workflows-fixed/ci.yml` in this repository.

## What This Fix Does
- ✅ Fetches the Lidarr submodule during CI checkout
- ✅ Provides real Lidarr project references for compilation
- ✅ Removes mock DLLs that can't be compiled against
- ✅ Uses only .NET 6 (what Lidarr actually uses)

## Expected Result
After this fix, the CI will:
1. Fetch the repository WITH the Lidarr submodule
2. Build successfully against real Lidarr projects
3. Pass all tests
4. Complete the security scan

## Why Manual Update Required
GitHub Apps (like Claude Code) cannot modify workflow files without special permissions. This is a security feature to prevent automated changes to CI/CD pipelines.