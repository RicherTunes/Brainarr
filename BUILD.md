# Building Brainarr Plugin

## Prerequisites

1. **.NET 6.0 SDK** or later
2. **Internet connection** (to download Lidarr DLLs)
3. **Git** (to clone the repository)

## Quick Build

### Option 1: Download Lidarr DLLs (Recommended)
Use the included setup scripts to download the required Lidarr DLLs:

```bash
# Clone the repository
git clone https://github.com/your-org/Brainarr.git
cd Brainarr

# Download Lidarr DLLs (Linux/macOS)
./setup-lidarr-dlls.sh

# Download Lidarr DLLs (Windows PowerShell)
.\setup-lidarr-dlls.ps1

# Build the plugin
dotnet build Brainarr.sln -c Release
```

### Option 2: Use Local Lidarr Installation
If you have Lidarr installed locally, set the `LIDARR_PATH` environment variable:

**Windows:**
```cmd
set LIDARR_PATH=C:\ProgramData\Lidarr\bin
dotnet build Brainarr.sln -c Release
```

**Linux/macOS:**
```bash
export LIDARR_PATH=/opt/Lidarr
dotnet build Brainarr.sln -c Release
```

**PowerShell:**
```powershell
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"
dotnet build Brainarr.sln -c Release
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
```
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
2. Copy files to a deployment folder
3. Create a ZIP package for distribution

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
2. Build main plugin: `dotnet build Brainarr.Plugin`
3. Run tests: `dotnet test Brainarr.Tests`

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