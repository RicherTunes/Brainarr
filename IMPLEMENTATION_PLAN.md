# Tubifarry Approach Implementation Plan

## Overview
Refactor Brainarr to use Tubifarry's proven approach: git submodules + ProjectReferences instead of Docker extraction + assembly References.

## Current State
- ✅ `ext/lidarr.plugin.common` already a submodule
- ❌ Docker extraction for Lidarr assemblies
- ❌ Assembly References with complex fallback logic
- ❌ Multi-targeting (net6.0 + net8.0)
- ❌ Per-project configuration

## Target State
- ✅ `ext/lidarr.plugin.common` submodule (keep)
- ✅ `ext/Lidarr` as new submodule (add)
- ✅ ProjectReferences to Lidarr source
- ✅ Single target framework (net8.0)
- ✅ Directory.Build.props for shared config
- ✅ Simplified CI workflows

## Implementation Steps

### Phase 1: Add Lidarr Source Submodule
1. Add Lidarr as git submodule at `ext/Lidarr`
2. Initialize and update submodule
3. Verify submodule structure

### Phase 2: Create Shared Configuration
1. Create `Directory.Build.props` at repository root
2. Define shared properties (BrainarrRootDir, PluginProject, etc.)
3. Configure output paths
4. Set common build properties

### Phase 3: Update Project Files
1. Change `<TargetFrameworks>net6.0;net8.0</TargetFrameworks>` to `<TargetFramework>net8.0</TargetFramework>`
2. Replace assembly `<Reference>` with `<ProjectReference>` to Lidarr source
3. Remove 157 lines of LidarrPath resolution logic
4. Remove CI fallback logic
5. Simplify to leverage Directory.Build.props

**Files to update:**
- `Brainarr.Plugin/Brainarr.Plugin.csproj`
- `Brainarr.Tests/Brainarr.Tests.csproj`
- `tests/Brainarr.TestKit.Providers/Brainarr.TestKit.Providers.csproj`
- `tests/Brainarr.Providers.OpenAI.Tests/Brainarr.Providers.OpenAI.Tests.csproj`

### Phase 4: Update NuGet Configuration
1. Add all required package sources (including private Azure feeds)
2. Remove complex package source mapping (not needed with ProjectReferences)
3. Follow Tubifarry's minimal approach

### Phase 5: Simplify CI Workflows
1. Update checkout to include `submodules: 'recursive'`
2. Remove Docker extraction steps
3. Remove assembly artifact upload/download
4. Remove verification steps for extracted assemblies
5. Simplify build steps (no LIDARR_PATH needed)

**Workflows to update:**
- `.github/workflows/ci.yml`
- `.github/workflows/test-and-coverage.yml`
- `.github/workflows/registry.yml`
- `.github/workflows/sanity-build.yml`

### Phase 6: Clean Up
1. Remove `scripts/extract-lidarr-assemblies.sh` (no longer needed)
2. Remove `scripts/ci/check-assemblies.sh` (no longer needed)
3. Update documentation
4. Remove diagnostic workflows (sanity-build.yml, registry.yml if not needed)

### Phase 7: Testing
1. Test local build: `dotnet build Brainarr.sln`
2. Test local tests: `dotnet test Brainarr.sln`
3. Push and verify CI workflows
4. Verify all matrix combinations pass

## Expected Benefits
- ✅ 500+ fewer lines of CI configuration
- ✅ No Docker extraction (faster, more reliable)
- ✅ Perfect version compatibility with Lidarr
- ✅ Works identically on all platforms
- ✅ Simpler to maintain
- ✅ Standard MSBuild patterns

## Rollback Plan
If issues arise:
1. Git branch is preserved
2. Can revert commits
3. Previous approach in git history

## Success Criteria
- ✅ Local build succeeds
- ✅ Local tests pass
- ✅ CI workflows pass on all platforms
- ✅ Reduced CI complexity
- ✅ No Docker extraction needed
