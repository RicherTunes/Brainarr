# Brainarr Plugin Development Guide

## Prerequisites

- .NET SDK 6.0+ (8.0 recommended)
- Git
- PowerShell (Windows) or Bash (Linux/macOS)

## Quick Start

### Windows
```powershell
# First time setup (fetch Lidarr assemblies, restore, build)
./setup.ps1

# Regular build
./build.ps1

# Build, test, and package
./build.ps1 -Test -Package
```

### Linux/macOS
```bash
# Make scripts executable (first time only)
chmod +x setup.sh build.sh

# First time setup (fetch Lidarr assemblies, restore, build)
./setup.sh

# Regular build
./build.sh

# Build, test, and package
./build.sh --test --package
```

## Project Structure

```text
Brainarr/
├── Brainarr.Plugin/          # Main plugin project
│   ├── Configuration/        # Settings and provider configs
│   ├── Services/            # Core services and providers
│   └── BrainarrImportList.cs # Main integration point
├── Brainarr.Tests/          # Unit and integration tests
├── ext/                     # External dependencies (gitignored)
│   └── Lidarr/             # Lidarr source checkout
├── .github/workflows/       # CI/CD pipelines
├── docs/                    # Documentation
└── plugin.json              # Plugin manifest (see below)
```

## Plugin Manifest

The `plugin.json` file defines plugin metadata for Lidarr:

```json
{
  "name": "Brainarr",
  "version": "1.3.0",
  "description": "AI-powered music discovery",
  "author": "Brainarr Team",
  "minimumVersion": "2.14.2.4786",
  "entryPoint": "Lidarr.Plugin.Brainarr.dll"
}
```

**Important**: Update the version field when releasing new versions.

## Building from Source

### Method 1: Using Build Scripts (Recommended)

The build scripts handle all dependencies automatically:

#### Windows (PowerShell)
```powershell
# Available parameters:
# -Setup         : Bootstrap Lidarr assemblies (first time only)
# -Test          : Run all tests after build
# -Package       : Create deployment package (.zip)
# -Clean         : Clean build artifacts before building
# -Deploy        : Deploy to local Lidarr instance
# -Configuration : Build configuration (Release/Debug, default: Release)
# -DeployPath    : Custom deployment path (default: X:\lidarr-hotio-test2\plugins\RicherTunes\Brainarr)

# Examples:
.\build.ps1 -Setup                    # First time setup
.\build.ps1                            # Standard build
.\build.ps1 -Test -Package            # Build, test, and package
.\build.ps1 -Clean -Configuration Debug  # Clean debug build
.\build.ps1 -Deploy -DeployPath "C:\ProgramData\Lidarr\plugins\Brainarr"  # Deploy to custom path
```

#### Linux/macOS (Bash)
```bash
# Available parameters:
# --setup       : Bootstrap Lidarr assemblies (first time only)
# --test        : Run all tests after build
# --package     : Create deployment package (.tar.gz)
# --clean       : Clean build artifacts before building
# --deploy      : Deploy to local Lidarr instance
# --debug       : Build in Debug configuration (default: Release)
# --deploy-path : Custom deployment path

# Examples:
./build.sh --setup                    # First time setup
./build.sh                             # Standard build
./build.sh --test --package           # Build, test, and package
./build.sh --clean --debug            # Clean debug build
./build.sh --deploy --deploy-path "/var/lib/lidarr/plugins/Brainarr"  # Deploy to custom path
```

### Method 2: Manual Build

1. **Preferred:** Extract Lidarr plugins/nightly assemblies from Docker (no source clone required)
```bash
bash ./scripts/extract-lidarr-assemblies.sh
export LIDARR_PATH="$(pwd)/ext/Lidarr-docker/_output/net8.0"
```

2. **Alternative (advanced): Clone Lidarr source:**
```bash
git clone --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr
cd ext/Lidarr
dotnet build -c Release
cd ../..
```

3. **Set LIDARR_PATH environment variable:**
```bash
# Linux/macOS
export LIDARR_PATH="$(pwd)/ext/Lidarr/_output/net8.0"

# Windows PowerShell
$env:LIDARR_PATH = "$(Get-Location)\ext\Lidarr\_output\net8.0"
```

4. **Build the plugin:**
```bash
cd Brainarr.Plugin
dotnet restore
dotnet build -c Release
```

## Development Workflow

### 1. Library-Aware Architecture

The plugin now uses an intelligent library-aware recommendation system:

- **LibraryAwarePromptBuilder**: Samples existing library intelligently
- **IterativeRecommendationStrategy**: Iterates if duplicates are returned
- **Provider-specific optimization**: More tokens for premium providers

### 2. Provider Configuration

Unified settings interface that adapts based on selected provider:
- Local providers (Ollama, LM Studio): URL + Model selection
- Cloud providers: API Key + Model selection
- Auto-detection for local models

### 3. Testing

```bash
# Run all tests
dotnet test

# Run specific test category
dotnet test --filter Category=Integration
dotnet test --filter Category=UnitTest

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### 4. Debugging

1. **Local Lidarr Instance:**
   - Install Lidarr locally
   - Copy plugin DLL to `<Lidarr>/Plugins/` directory
   - Restart Lidarr
   - Check logs at `<Lidarr>/logs/`

2. **Enable Debug Logging:**
   ```csharp
   _logger.Debug($"Detailed info: {variable}");
   ```

3. **Test Connection:**
   - In Lidarr UI: Settings → Import Lists → Brainarr → Test

## Configuration Options

### Sampling Strategy (New)
```csharp
public enum SamplingStrategy
{
    Minimal,     // Small sample for local models
    Balanced,    // Default - balanced for most cases
    Comprehensive // Large sample for premium providers
}
```

### Prompt Budgets
- Brainarr applies provider‑specific budgets and a headroom guard. Actual limits vary by model and provider; see `docs/configuration.md` and the provider’s documentation.

### Feedback Loop
The plugin tracks:
- Success rate per provider
- Duplicate rate
- Response times
- Cache hit rates

## CI/CD Pipeline

GitHub Actions jobs extract Lidarr assemblies from Docker image `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` into `ext/Lidarr-docker/_output/net8.0/`, build Brainarr against those binaries, run tests, and publish release artifacts.

## Troubleshooting

### Build Errors

**"Lidarr installation not found"**
- Run `.\build.ps1 -Setup` or `./build.sh --setup`
- Or set `LIDARR_PATH` environment variable manually

**"The type or namespace name 'NzbDrone' does not exist"**
- Lidarr DLLs not found
- Check LIDARR_PATH points to correct directory
- Ensure Lidarr is built successfully

### Runtime Errors

**"No AI provider configured"**
- Check provider settings in Lidarr UI
- Ensure API keys are set for cloud providers
- Verify local providers are running

**"Failed to auto-detect models"**
- For Ollama: Ensure Ollama is running (`ollama serve`)
- For LM Studio: Ensure server is started
- Check firewall/network settings

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests
5. Submit a pull request

## License

This project is licensed under the MIT License - see LICENSE file for details.
