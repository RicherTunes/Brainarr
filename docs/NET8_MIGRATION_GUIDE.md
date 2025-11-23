# .NET 8.0 Multi-Targeting Migration Guide

This guide shows how to add .NET 8.0 support to Lidarr plugins to fix TypeLoadException issues (like issue #268).

## Quick Diagnosis

Run this command in your plugin repository:

```bash
grep -n "TargetFramework" YourPlugin.Plugin/YourPlugin.Plugin.csproj
```

**Result indicates**:
- ❌ `<TargetFramework>net6.0</TargetFramework>` → **Needs migration**
- ✅ `<TargetFrameworks>net6.0;net8.0</TargetFrameworks>` → **Already updated**

## Migration Steps

### Step 1: Update Plugin .csproj

**File**: `YourPlugin.Plugin/YourPlugin.Plugin.csproj`

**Change**:
```xml
<!-- Before -->
<TargetFramework>net6.0</TargetFramework>

<!-- After -->
<TargetFrameworks>net6.0;net8.0</TargetFrameworks>
```

**Update LidarrPath resolution** (find the section with LidarrPath conditions):
```xml
<!-- Before -->
<LidarrPath Condition="'$(LidarrPath)' == '' AND Exists('..\ext\Lidarr-docker\_output\net6.0')">..\ext\Lidarr-docker\_output\net6.0</LidarrPath>

<!-- After -->
<LidarrPath Condition="'$(LidarrPath)' == '' AND Exists('..\ext\Lidarr-docker\_output\$(TargetFramework)')">..\ext\Lidarr-docker\_output\$(TargetFramework)</LidarrPath>
```

Replace ALL hardcoded `net6.0` paths with `$(TargetFramework)`:
- `../ext/Lidarr-docker/_output/net6.0` → `../ext/Lidarr-docker/_output/$(TargetFramework)`
- `../ext/Lidarr/_output/net6.0` → `../ext/Lidarr/_output/$(TargetFramework)`
- `../ext/Lidarr/src/Lidarr/bin/Release/net6.0` → `../ext/Lidarr/src/Lidarr/bin/Release/$(TargetFramework)`

### Step 2: Update Directory.Packages.props

**File**: `Directory.Packages.props`

**Before**:
```xml
<ItemGroup>
  <PackageVersion Include="Microsoft.Extensions.Primitives" Version="7.0.0" />
  <PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="6.0.3" />
  <!-- ... other packages ... -->
</ItemGroup>
```

**After** (add framework-specific versions):
```xml
<ItemGroup>
  <!-- Core Dependencies (framework-agnostic) -->
  <PackageVersion Include="Newtonsoft.Json" Version="13.0.4" />
  <PackageVersion Include="FluentValidation" Version="9.5.4" />
  <PackageVersion Include="NLog" Version="6.0.3" />
  <PackageVersion Include="System.Security.Cryptography.ProtectedData" Version="6.0.0" />
  <PackageVersion Include="System.Configuration.ConfigurationManager" Version="6.0.1" />
  <!-- Test Dependencies -->
  <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  <PackageVersion Include="xunit" Version="2.9.3" />
  <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.2" />
  <PackageVersion Include="coverlet.collector" Version="6.0.4" />
  <PackageVersion Include="Moq" Version="4.20.72" />
  <PackageVersion Include="FluentAssertions" Version="8.6.0" />
  <PackageVersion Include="Bogus" Version="35.6.3" />
</ItemGroup>

<!-- .NET 6 specific package versions -->
<ItemGroup Condition="'$(TargetFramework)' == 'net6.0'">
  <PackageVersion Include="Microsoft.Extensions.Primitives" Version="6.0.0" />
  <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="6.0.0" />
  <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="6.0.2" />
  <PackageVersion Include="Microsoft.Extensions.Options" Version="6.0.0" />
  <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="6.0.1" />
  <PackageVersion Include="Microsoft.Extensions.Caching.Abstractions" Version="6.0.0" />
  <PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="6.0.3" />
  <PackageVersion Include="System.Diagnostics.DiagnosticSource" Version="6.0.1" />
</ItemGroup>

<!-- .NET 8 specific package versions -->
<ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
  <PackageVersion Include="Microsoft.Extensions.Primitives" Version="8.0.0" />
  <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.1" />
  <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
  <PackageVersion Include="Microsoft.Extensions.Options" Version="8.0.2" />
  <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />
  <PackageVersion Include="Microsoft.Extensions.Caching.Abstractions" Version="8.0.0" />
  <PackageVersion Include="Microsoft.Extensions.Caching.Memory" Version="8.0.1" />
  <PackageVersion Include="System.Diagnostics.DiagnosticSource" Version="8.0.1" />
</ItemGroup>
```

### Step 3: Update lidarr.plugin.common Submodule

```bash
cd ext/lidarr.plugin.common
git fetch origin
git checkout main
git pull origin main
cd ../..
git add ext/lidarr.plugin.common
```

**Verify the version**:
```bash
cd ext/lidarr.plugin.common
git log -1 --oneline
# Should show: 7591909 or later (v1.2.2+)
```

### Step 4: Update CI Workflow

**File**: `.github/workflows/ci.yml`

**In the `prepare-lidarr` job**, update assembly extraction:

```yaml
# Before (single extraction)
- name: Extract Lidarr assemblies (shared script)
  run: timeout 12m bash scripts/extract-lidarr-assemblies.sh --mode full --no-tar-fallback --output-dir ext/Lidarr-docker/_output/net6.0
  shell: bash

- name: Upload assemblies artifact
  uses: actions/upload-artifact@v4
  with:
    name: lidarr-assemblies
    path: ext/Lidarr-docker/_output/net6.0/
    if-no-files-found: error

# After (dual extraction)
- name: Extract Lidarr assemblies for .NET 6.0
  run: timeout 12m bash scripts/extract-lidarr-assemblies.sh --mode full --no-tar-fallback --output-dir ext/Lidarr-docker/_output/net6.0
  shell: bash

- name: Extract Lidarr assemblies for .NET 8.0
  run: timeout 12m bash scripts/extract-lidarr-assemblies.sh --mode full --no-tar-fallback --output-dir ext/Lidarr-docker/_output/net8.0
  shell: bash

- name: Upload .NET 6.0 assemblies artifact
  uses: actions/upload-artifact@v4
  with:
    name: lidarr-assemblies-net6.0
    path: ext/Lidarr-docker/_output/net6.0/
    if-no-files-found: error

- name: Upload .NET 8.0 assemblies artifact
  uses: actions/upload-artifact@v4
  with:
    name: lidarr-assemblies-net8.0
    path: ext/Lidarr-docker/_output/net8.0/
    if-no-files-found: error
```

**In the `test` job**, update artifact downloads:

```yaml
# Before (single download)
- name: Download Lidarr assemblies artifact
  uses: actions/download-artifact@v4
  with:
    name: lidarr-assemblies
    path: ext/Lidarr-docker/_output/net6.0/

# After (dual download)
- name: Download .NET 6.0 assemblies artifact
  uses: actions/download-artifact@v4
  with:
    name: lidarr-assemblies-net6.0
    path: ext/Lidarr-docker/_output/net6.0/

- name: Download .NET 8.0 assemblies artifact
  uses: actions/download-artifact@v4
  with:
    name: lidarr-assemblies-net8.0
    path: ext/Lidarr-docker/_output/net8.0/
```

Add verification steps for both:

```yaml
- name: Verify .NET 6.0 assemblies present
  shell: bash
  run: |
    set -euo pipefail
    test -f ext/Lidarr-docker/_output/net6.0/Lidarr.Core.dll || { echo "Missing Lidarr.Core.dll in .NET 6.0 assemblies"; exit 1; }
    ls -la ext/Lidarr-docker/_output/net6.0/

- name: Verify .NET 8.0 assemblies present
  shell: bash
  run: |
    set -euo pipefail
    test -f ext/Lidarr-docker/_output/net8.0/Lidarr.Core.dll || { echo "Missing Lidarr.Core.dll in .NET 8.0 assemblies"; exit 1; }
    ls -la ext/Lidarr-docker/_output/net8.0/
```

### Step 5: Update Release Workflow

**File**: `.github/workflows/release.yml`

**Update .NET SDK setup**:
```yaml
# Before
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '6.0.x'

# After
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: |
      6.0.x
      8.0.x
```

**Update assembly extraction**:
```yaml
# Before
- name: Extract Lidarr assemblies (plugins Docker)
  shell: bash
  run: |
    set -euo pipefail
    bash scripts/extract-lidarr-assemblies.sh --mode minimal --output-dir ext/Lidarr-docker/_output/net6.0

# After
- name: Extract Lidarr assemblies for .NET 6.0
  shell: bash
  run: |
    set -euo pipefail
    bash scripts/extract-lidarr-assemblies.sh --mode minimal --output-dir ext/Lidarr-docker/_output/net6.0

- name: Extract Lidarr assemblies for .NET 8.0
  shell: bash
  run: |
    set -euo pipefail
    bash scripts/extract-lidarr-assemblies.sh --mode minimal --output-dir ext/Lidarr-docker/_output/net8.0
```

**Update packaging** (creates both net6.0 and net8.0 folders):
```yaml
# Before
- name: Package Plugin
  if: steps.check_release.outputs.exists == 'false'
  shell: bash
  run: |
    mkdir -p release
    BUILD_PATH="Brainarr.Plugin/bin/Release/net6.0/"
    cp "${BUILD_PATH}Lidarr.Plugin.YourPlugin.dll" release/
    cp plugin.json release/

# After
- name: Package Plugin
  if: steps.check_release.outputs.exists == 'false'
  shell: bash
  run: |
    mkdir -p release/net6.0 release/net8.0

    # Package .NET 6.0 build
    BUILD_PATH_NET6="YourPlugin.Plugin/bin/Release/net6.0/"
    cp "${BUILD_PATH_NET6}Lidarr.Plugin.YourPlugin.dll" release/net6.0/
    cp "${BUILD_PATH_NET6}"*.dll release/net6.0/ 2>/dev/null || true

    # Package .NET 8.0 build
    BUILD_PATH_NET8="YourPlugin.Plugin/bin/Release/net8.0/"
    cp "${BUILD_PATH_NET8}Lidarr.Plugin.YourPlugin.dll" release/net8.0/
    cp "${BUILD_PATH_NET8}"*.dll release/net8.0/ 2>/dev/null || true

    # Copy plugin.json to root
    cp plugin.json release/
    cp README.md release/
    cp LICENSE release/
    cp CHANGELOG.md release/
```

### Step 6: Add Plugin Loading Tests

Create `YourPlugin.Tests/PluginLoadingTests.cs` (copy from Brainarr's implementation).

Key tests to include:
- Plugin assembly should be compiled for correct target framework
- Plugin should instantiate without TypeLoadException
- Test method should be accessible
- Dependencies should load without conflicts

### Step 7: Verify the Changes

```bash
# Build for both frameworks
dotnet build -c Release

# Verify both outputs exist
ls -la YourPlugin.Plugin/bin/Release/net6.0/
ls -la YourPlugin.Plugin/bin/Release/net8.0/

# Run tests
dotnet test

# Check for the PluginLoading test category
dotnet test --filter Category=PluginLoading
```

## Automated Migration Script

Save this as `migrate-to-net8.sh`:

```bash
#!/bin/bash
set -euo pipefail

PLUGIN_NAME="${1:-YourPlugin}"

echo "🔧 Migrating ${PLUGIN_NAME} to .NET 8.0 multi-targeting..."

# 1. Update .csproj
echo "📝 Updating ${PLUGIN_NAME}.Plugin.csproj..."
sed -i 's/<TargetFramework>net6.0<\/TargetFramework>/<TargetFrameworks>net6.0;net8.0<\/TargetFrameworks>/' "${PLUGIN_NAME}.Plugin/${PLUGIN_NAME}.Plugin.csproj"
sed -i 's/\\net6.0/\\$(TargetFramework)/g' "${PLUGIN_NAME}.Plugin/${PLUGIN_NAME}.Plugin.csproj"
sed -i 's/_output\/net6.0/_output\/$(TargetFramework)/g' "${PLUGIN_NAME}.Plugin/${PLUGIN_NAME}.Plugin.csproj"

# 2. Update submodule
echo "📦 Updating lidarr.plugin.common submodule..."
cd ext/lidarr.plugin.common
git fetch origin
git checkout main
git pull origin main
cd ../..

# 3. Show what needs manual updates
echo "⚠️  Manual steps required:"
echo "   1. Update Directory.Packages.props with framework-specific package versions"
echo "   2. Update .github/workflows/ci.yml for dual assembly extraction"
echo "   3. Update .github/workflows/release.yml for dual packaging"
echo "   4. Add PluginLoadingTests.cs"
echo ""
echo "✅ Automated changes complete!"
echo "📖 See docs/NET8_MIGRATION_GUIDE.md for manual steps"
```

Usage:
```bash
chmod +x migrate-to-net8.sh
./migrate-to-net8.sh Tidalarr
./migrate-to-net8.sh Qobuzarr
```

## Validation Checklist

After migration, verify:

- [ ] `grep "TargetFrameworks.*net6.0.*net8.0" YourPlugin.Plugin/YourPlugin.Plugin.csproj` returns a match
- [ ] `Directory.Packages.props` has conditional ItemGroups for both frameworks
- [ ] CI extracts both net6.0 and net8.0 assemblies
- [ ] Release workflow packages both frameworks
- [ ] `dotnet build` produces both `bin/Release/net6.0/` and `bin/Release/net8.0/`
- [ ] All tests pass on both frameworks
- [ ] Plugin loading tests are present and passing

## Troubleshooting

### Build Error: "Could not find Lidarr.Core.dll"

**Cause**: LidarrPath not resolving correctly for the target framework.

**Fix**: Ensure `$(TargetFramework)` is used in all LidarrPath conditions.

### Test Error: "Missing Public API baseline"

**Cause**: lidarr.plugin.common submodule is outdated.

**Fix**: Update to v1.2.2+:
```bash
cd ext/lidarr.plugin.common && git pull origin main && cd ../..
```

### NuGet Error: "Package version not found"

**Cause**: Missing framework-specific package versions.

**Fix**: Add both .NET 6 and .NET 8 conditional ItemGroups in `Directory.Packages.props`.

## References

- [Brainarr PR #269](https://github.com/RicherTunes/Brainarr/pull/269) - Complete implementation
- [Issue #268](https://github.com/RicherTunes/Brainarr/issues/268) - Original bug report
- [Lidarr.Plugin.Common v1.2.2](https://github.com/RicherTunes/Lidarr.Plugin.Common/releases/tag/v1.2.2) - Required version
