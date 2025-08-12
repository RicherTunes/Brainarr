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
# Windows - This will clone and build real Lidarr
.\setup-lidarr.ps1

# Linux/macOS
chmod +x setup-lidarr.sh
./setup-lidarr.sh
```

### Option 2: Manual Setup

1. Clone Lidarr's plugin branch:
```bash
git clone --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr
```

2. Build Lidarr:
```bash
cd ext/Lidarr
dotnet restore
dotnet build -c Release
cd ../..
```

3. Set environment variable:
```bash
# Find where Lidarr built to (usually _output/net6.0 or src/Lidarr/bin/Release/net6.0)
export LIDARR_PATH=/path/to/lidarr/build/output
```

4. Now build Brainarr:
```bash
cd Brainarr.Plugin
dotnet build -c Release
```

## Build Will Fail Without Lidarr

The build is **designed to fail** if Lidarr is not found:

```
Error: Lidarr installation not found. Run '.\setup-lidarr.ps1' or set LIDARR_PATH
```

This is intentional! We want compilation to fail fast rather than runtime failures later.

## CI/CD Integration

Our GitHub Actions workflow automatically:
1. Clones real Lidarr source
2. Builds Lidarr
3. Builds Brainarr against real Lidarr
4. Runs integration tests with real types

See `.github/workflows/build.yml` for the complete pipeline.

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

## Never Use Stubs Because...

Real integration means:
- **Compile-time validation** of all Lidarr API usage
- **Accurate IntelliSense** for development
- **Real type checking** prevents runtime surprises
- **API compatibility** verified at build time
- **Integration tests** run against actual Lidarr code

Remember: **If it doesn't compile against real Lidarr, it won't work in production!**