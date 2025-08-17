# CI/CD Build Failure Analysis and Resolution

## Current Issues

### 1. CodeQL Security Scan Failure
**Error**: "CodeQL detected code written in C# but could not process any of it"
**Cause**: CodeQL runs autobuild which uses `dotnet build` on the solution file, but it's not finding any C# code to analyze
**Solution**: Ensure the build step in security-scan job actually builds the projects

### 2. Missing Lidarr Assemblies on Linux
**Error**: Cannot resolve Lidarr.Core, Lidarr.Common, etc.
**Cause**: The Lidarr build output path on Linux includes the runtime identifier (linux-x64) but the project expects just net6.0
**Solution**: Update the LIDARR_PATH environment variable to include the runtime identifier on Linux

### 3. No Test Results Being Generated
**Warning**: "No files were found with the provided path: TestResults/"
**Cause**: Tests aren't actually running - the solution file builds but doesn't execute tests
**Solution**: Ensure test projects are properly referenced and tests are discovered

## Required CI Workflow Changes

```yaml
# Update the Set Lidarr Path step to handle Linux runtime identifier
- name: Set Lidarr Path
  run: |
    if [ "${{ runner.os }}" == "Linux" ]; then
      echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/linux-x64" >> $GITHUB_ENV
    elif [ "${{ runner.os }}" == "Windows" ]; then
      echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/win-x64" >> $GITHUB_ENV
    else
      echo "LIDARR_PATH=${{ github.workspace }}/ext/Lidarr/_output/net6.0/osx-x64" >> $GITHUB_ENV
    fi
  shell: bash
```

## Manual Application Instructions

Since GitHub Apps cannot modify workflow files, you need to apply these changes manually:

1. **Create a new branch from main**:
   ```bash
   git checkout main
   git pull origin main
   git checkout -b fix/ci-lidarr-path
   ```

2. **Apply the workflow fix**:
   Edit `.github/workflows/ci.yml` and update ALL occurrences of the "Set Lidarr Path" step (lines 60-62, 119-121, 181-183) with the platform-specific version above.

3. **Commit and push**:
   ```bash
   git add .github/workflows/ci.yml
   git commit -m "fix: update Lidarr path for platform-specific runtime identifiers"
   git push origin fix/ci-lidarr-path
   ```

4. **Create PR and merge**:
   - Create a PR from `fix/ci-lidarr-path` to `main`
   - Once CI passes, merge the PR

## Alternative Quick Fix

If you want to unblock the current PR immediately, you can:

1. Temporarily disable the security-scan job by commenting it out
2. Fix only the Linux path since that's what's currently in the matrix
3. Add Windows and macOS back to the matrix later

## Verification Steps

After applying the fix:
1. Check that the build succeeds on Linux
2. Verify Lidarr assemblies are found at the correct path
3. Ensure tests actually run and generate results
4. Confirm CodeQL can analyze the built code

## Root Cause Summary

The main issue is that Lidarr's build script generates platform-specific output directories:
- Linux: `_output/net6.0/linux-x64/`
- Windows: `_output/net6.0/win-x64/`
- macOS: `_output/net6.0/osx-x64/`

But our CI workflow was expecting just `_output/net6.0/`, which doesn't exist after the build completes.