# Build Requirements - NO STUBS, REAL LIDARR ONLY

## Critical Requirement

**This plugin REQUIRES actual Lidarr source code to build. We do NOT use stubs, mocks, or placeholders.**

## Why No Stubs?

1. **Hidden Bugs**: Stubs hide real integration issues that only appear at runtime
2. **False Confidence**: Code might compile but fail catastrophically when deployed
3. **API Mismatches**: Lidarr's internal APIs change - stubs become outdated
4. **Type Safety**: Real types catch errors at compile time, not runtime
5. **Integration Testing**: Can't test real behavior with fake interfaces

## Setup Instructions

### Option 1: Automated Setup (Recommended)

```powershell
# Windows
./setup.ps1

# Linux/macOS
chmod +x ./setup.sh
./setup.sh
```

### Option 2: Manual Setup

1. Prefer Docker-based extraction of the plugins/nightly assemblies (no full source clone required):

```bash
bash ./scripts/extract-lidarr-assemblies.sh
# Assemblies land in ext/Lidarr-docker/_output/net6.0/
```

2. Alternatively, clone Lidarr's plugins branch (advanced):

```bash
git clone --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr
```

3. Build Lidarr (only if cloning source):

```bash
cd ext/Lidarr
dotnet restore
dotnet build -c Release
cd ../..
```

4. Set environment variable:

```bash
# Prefer Docker output: ext/Lidarr-docker/_output/net6.0
export LIDARR_PATH="$(pwd)/ext/Lidarr-docker/_output/net6.0"
```

5. Now build Brainarr:

```bash
cd Brainarr.Plugin
dotnet build -c Release
```

## Build Will Fail Without Lidarr

The build is **designed to fail** if Lidarr is not found:

```text
Error: Lidarr installation not found. Run '.\setup-lidarr.ps1' or set LIDARR_PATH
```

This is intentional! We want compilation to fail fast rather than runtime failures later.

## CI/CD Integration

Our GitHub Actions workflows extract real Lidarr assemblies from Docker image `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` into `ext/Lidarr-docker/_output/net6.0/`, then build Brainarr against those binaries and run tests. Jobs fail fast if assemblies are missing.

See `.github/workflows/` (`plugin-package.yml`, `test-and-coverage.yml`, `sanity-build.yml`) for the pipelines.

## For Developers

If you're getting build errors about missing types like `IImportListSettings`, `ImportListBase`, etc:

- ✅ **CORRECT ACTION**: Run setup-lidarr.ps1 to get real Lidarr
- ❌ **WRONG ACTION**: Creating stub interfaces or dummy types
- ❌ **WRONG ACTION**: Commenting out code that doesn't compile

## Troubleshooting

### "The type or namespace name 'NzbDrone' does not exist"

- You don't have Lidarr. Run: `.\setup-lidarr.ps1`

### "Could not load file or assembly 'Lidarr.Core'"

- LIDARR_PATH is wrong. Check it points to folder with Lidarr.Core.dll

### "Lidarr installation not found"

- This is the expected error when Lidarr is missing
- Solution: Run the setup script

## Never Use Stubs Because

Real integration means:

- **Compile-time validation** of all Lidarr API usage
- **Accurate IntelliSense** for development
- **Real type checking** prevents runtime surprises
- **API compatibility** verified at build time
- **Integration tests** run against actual Lidarr code

Remember: **If it doesn't compile against real Lidarr, it won't work in production!**
