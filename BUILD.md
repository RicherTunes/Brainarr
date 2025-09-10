# Building Brainarr Plugin

## Prerequisites

1. **.NET 6.0 SDK** or later
1. **Lidarr installation** (the plugin needs Lidarr assemblies to compile)
1. **Git** (to clone the repository)

## Quick Build

### Option 1: Automatic Detection (Recommended)

The build system will automatically detect Lidarr in common installation paths:

```bash
# Extract or clone the project
cd Brainarr
cd Brainarr.Plugin
dotnet build -c Release
```

### Option 2: Environment Variable

If Lidarr is installed in a custom location, set the `LIDARR_PATH` environment variable:

**Windows:**

```cmd
set LIDARR_PATH=C:\ProgramData\Lidarr\bin
cd Brainarr.Plugin
dotnet build -c Release
```

**Linux/macOS:**

```bash
export LIDARR_PATH=/opt/Lidarr
cd Brainarr.Plugin
dotnet build -c Release
```

**PowerShell:**

```powershell
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"
cd Brainarr.Plugin
dotnet build -c Release
```

## Common Lidarr Installation Paths

### Windows

- **Installer**: `C:\ProgramData\Lidarr\bin`
- **Portable**: `[Lidarr folder]\bin`
- **Scoop**: `%USERPROFILE%\scoop\apps\lidarr\current`

### Linux

- **Package Manager**: `/usr/lib/lidarr/bin`
- **Manual Install**: `/opt/Lidarr`
- **Snap**: `/snap/lidarr/current`

### Docker

- **Host Path**: Map container `/usr/lib/lidarr/bin` to host
- **Inside Container**: `/usr/lib/lidarr/bin`

## Build Output

After successful build, the plugin files will be in:

```text
Brainarr.Plugin/bin/
├── Lidarr.Plugin.Brainarr.dll    # Main plugin
├── plugin.json                   # Plugin manifest
├── [dependencies].dll            # NuGet packages
```

## Using build_and_deploy.ps1

For convenience, use the included PowerShell script:

```powershell
# Set Lidarr path if needed
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"

# Build and deploy
.\build_and_deploy.ps1
```

This script will:

1. Build the plugin in Release mode
1. Copy files to a deployment folder
1. Create a ZIP package for distribution

## Troubleshooting

### Error: "Lidarr installation not found"

- Set `LIDARR_PATH` environment variable to your Lidarr installation
- Verify Lidarr DLL files exist in the specified path
- Check that you have Lidarr v1.0+ installed

### Error: "Could not load file or assembly"

- Ensure you're using .NET 6.0 SDK
- Verify Lidarr assemblies are compatible version
- Try cleaning and rebuilding: `dotnet clean && dotnet build`

### Error: "Access denied" or permission issues

- Run command prompt as administrator (Windows)
- Check file permissions on Lidarr directory
- Ensure Lidarr is not running during build

## Development Setup

For development with full test suite:

1. Extract/setup repository
1. Build main plugin: `dotnet build Brainarr.Plugin`
1. Run tests: `dotnet test Brainarr.Tests`

## CI/CD Notes

For automated builds, set the `LIDARR_PATH` environment variable in your CI system:

**GitHub Actions:**

```yaml
env:
  LIDARR_PATH: /opt/Lidarr
```

**Docker Build:**

```dockerfile
ENV LIDARR_PATH=/usr/lib/lidarr/bin
```

## Tests and Coverage

Run the full suite locally with the helper script:

```powershell
./test-local-ci.ps1
```

Skip downloading Lidarr assemblies if you already have `ext/Lidarr/_output/net6.0`:

```powershell
./test-local-ci.ps1 -SkipDownload
```

For faster, more stable local runs (exclude heavy Performance/Stress tests) and generate an HTML coverage report:

```powershell
./test-local-ci.ps1 -SkipDownload -ExcludeHeavy -GenerateCoverageReport -InstallReportGenerator
```

This produces a Cobertura file under `TestResults/` and an HTML report at:

```
TestResults/CoverageReport/index.html
```

To generate a report from an existing test run:

```powershell
scripts/generate-coverage-report.ps1 -InstallTool
```

If you already have ReportGenerator installed globally (`dotnet tool install -g dotnet-reportgenerator-globaltool`), omit `-InstallTool`.
