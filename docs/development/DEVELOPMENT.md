# Brainarr Development Setup

## Overview

This guide covers setting up a development environment for contributing to Brainarr.

## Prerequisites

- **.NET SDK**: 8.0 (Lidarr plugin host is .NET 8)
- **IDE**: Visual Studio, VS Code, or JetBrains Rider (recommended)
- **Git**: For cloning the repository
- **PowerShell**: `pwsh` recommended (scripts use PowerShell)
- **Lidarr**: For plugin testing (use the repo `setup` scripts)

## Repository Structure

```
Brainarr/
├── Brainarr.Plugin/          # Main plugin code
├── Brainarr.Tests/           # Test suite
├── docs/                     # Documentation
├── wiki-content/             # GitHub Wiki source
└── ext/                      # Submodules (Lidarr + Lidarr.Plugin.Common)
```

## Initial Setup

### 1. Clone with Submodules

```bash
git clone --recursive https://github.com/RicherTunes/Brainarr.git
cd Brainarr
```

If you already cloned without `--recursive`:

```bash
git submodule update --init --recursive
```

### 2. Bootstrap Lidarr + Build

Brainarr builds against real Lidarr assemblies. Use the repository bootstrap script (recommended):

```powershell
./setup.ps1
```

This prepares `ext/Lidarr/_output/net8.0` and configures `LIDARR_PATH` for the build.

For release builds:

```powershell
dotnet build Brainarr.sln -c Release
```

## Running Tests

Brainarr uses the ecosystem unified test runner and standardized categories.

```powershell
# Default fast lane (excludes Integration/Packaging/LibraryLinking/Benchmark/Slow + Quarantined)
pwsh -File scripts/test.ps1

# Packaging/libraries lane (requires a package build/artifacts)
pwsh -File scripts/test-packaging.ps1
```

Standard categories:
- `Integration`
- `Packaging`
- `LibraryLinking`
- `Benchmark`
- `Slow`

## Development Workflow

### Making Changes

1. Create a feature branch: `git checkout -b my-feature`
2. Make your changes
3. Run tests: `dotnet test`
4. Build locally: `dotnet build -c Release`
5. Commit changes with clear messages

### Testing with Lidarr

1. Build the plugin in Release configuration
2. Copy output to Lidarr plugins directory:
   - **Windows**: `%ProgramData%\Lidarr\plugins\Brainarr\`
   - **Linux**: `/var/lib/lidarr/plugins/Brainarr/`
   - **macOS**: `~/Library/Application Support/Lidarr/plugins/Brainarr/`
3. Restart Lidarr
4. Configure and test in Settings > Import Lists > Add > Brainarr

## Debugging

Enable debug logging in Lidarr:
- Settings > General > Log Level: Debug
- Check System > Logs for plugin output

## CI/CD

The project uses GitHub Actions for continuous integration:
- Builds on push and pull requests
- Runs full test suite
- Performs security analysis with CodeQL

See [CI/CD Improvements](../CI_CD_IMPROVEMENTS.md) for details.

## Related Documentation

- [Testing Guide](../TESTING_GUIDE.md) - Comprehensive testing documentation
- [Architecture](../ARCHITECTURE.md) - System design overview
- [Contributing](../CONTRIBUTING.md) - Contribution guidelines
- [Wiki Sync](WIKI-SYNC.md) - Documentation synchronization process

---

**Last Updated**: January 2025
