# Brainarr Plugin Development Guide

## Prerequisites

- .NET 6.0 SDK or later
- Git
- PowerShell (Windows) or Bash (Linux/macOS)

## Quick Start

### Windows
```powershell
# First time setup - clones and builds Lidarr
.\build.ps1 -Setup

# Regular build
.\build.ps1

# Build, test, and package
.\build.ps1 -Test -Package
```

### Linux/macOS
```bash
# Make script executable
chmod +x build.sh

# First time setup - clones and builds Lidarr
./build.sh --setup

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
  "name": "Brainarr",              // Plugin display name
  "version": "1.0.0",               // Semantic version
  "description": "...",             // Short description
  "author": "Brainarr Team",        // Author information
  "minimumVersion": "4.0.0.0",     // Minimum Lidarr version required
  "entryPoint": "Lidarr.Plugin.Brainarr.dll"  // Main assembly file
}
```

**Important**: Update the version field when releasing new versions.

## Building from Source

### Method 1: Using Build Scripts (Recommended)

The build scripts handle all dependencies automatically:

#### Windows (PowerShell)
```powershell
# Available parameters:
# -Setup         : Clone and build Lidarr (first time only)
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
# --setup       : Clone and build Lidarr (first time only)
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

1. **Clone Lidarr source:**
```bash
git clone --branch plugins https://github.com/Lidarr/Lidarr.git ext/Lidarr
cd ext/Lidarr
dotnet build -c Release
cd ../..
```

2. **Set LIDARR_PATH environment variable:**
```bash
# Linux/macOS
export LIDARR_PATH="$(pwd)/ext/Lidarr/_output/net6.0"

# Windows PowerShell
$env:LIDARR_PATH = "$(Get-Location)\ext\Lidarr\_output\net6.0"
```

3. **Build the plugin:**
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

### Token Limits by Provider
- **Local (Ollama/LM Studio)**: ~2000 tokens
- **Budget (DeepSeek/Gemini)**: ~3000 tokens
- **Premium (GPT-4/Claude)**: ~4000 tokens

### Feedback Loop
The plugin tracks:
- Success rate per provider
- Duplicate rate
- Response times
- Cache hit rates

## CI/CD Pipeline

GitHub Actions workflow automatically:
1. Checks out Lidarr source
2. Builds Lidarr
3. Builds plugin against Lidarr
4. Runs tests
5. Creates release artifacts

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
