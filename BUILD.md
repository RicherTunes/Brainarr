# Build Instructions for Brainarr

This document provides step-by-step instructions for building the Brainarr plugin from source.

## Prerequisites

- .NET 6.0 SDK or later
- Node.js 18+ and Yarn (for building Lidarr from source)
- Git (for cloning the repository)

## Quick Start

### All Platforms

```bash
# Clone the repository with submodules
git clone --recursive https://github.com/RicherTunes/Brainarr.git
cd Brainarr

# Initialize and update submodules (if not done with --recursive)
git submodule update --init --recursive

# Build Lidarr from source (required for plugin development)
./scripts/setup-lidarr-deps.sh

# Build the plugin
dotnet build -c Release

# Run tests
dotnet test
```

## Building Lidarr from Source

The Brainarr plugin requires Lidarr assemblies to build. We build Lidarr from source to ensure compatibility:

```bash
# The setup script handles this automatically:
./scripts/setup-lidarr-deps.sh

# Or manually:
cd ext/Lidarr
yarn install
./build.sh --backend
cd ../..
export LIDARR_PATH="$(pwd)/ext/Lidarr/_output/net6.0"
```

## Manual Build Steps

1. **Ensure Lidarr is Built**
   ```bash
   # Check if Lidarr binaries exist
   ls ext/Lidarr/_output/net6.0/Lidarr.Core.dll
   
   # If not, build Lidarr:
   cd ext/Lidarr && ./build.sh --backend && cd ../..
   ```

2. **Set Lidarr Path** (if needed)
   ```bash
   export LIDARR_PATH="$(pwd)/ext/Lidarr/_output/net6.0"
   ```

3. **Restore Dependencies**
   ```bash
   dotnet restore
   ```

4. **Build the Plugin**
   ```bash
   dotnet build -c Release
   ```

5. **Run Tests**
   ```bash
   dotnet test
   ```

## Build Output

The compiled plugin will be in:
- `Brainarr.Plugin/bin/Release/net6.0/Lidarr.Plugin.Brainarr.dll`

## Deployment

Copy the plugin files to your Lidarr plugins directory:

### Windows
```powershell
Copy-Item "Brainarr.Plugin\bin\Release\net6.0\Lidarr.Plugin.Brainarr.dll" "C:\ProgramData\Lidarr\bin\plugins\"
Copy-Item "plugin.json" "C:\ProgramData\Lidarr\bin\plugins\"
```

### Linux
```bash
sudo cp Brainarr.Plugin/bin/Release/net6.0/Lidarr.Plugin.Brainarr.dll /opt/Lidarr/plugins/
sudo cp plugin.json /opt/Lidarr/plugins/
```

### macOS
```bash
cp Brainarr.Plugin/bin/Release/net6.0/Lidarr.Plugin.Brainarr.dll ~/Library/Application\ Support/Lidarr/plugins/
cp plugin.json ~/Library/Application\ Support/Lidarr/plugins/
```

## CI/CD

The GitHub Actions workflow automatically:
1. Builds Lidarr from source
2. Builds and tests the plugin on multiple platforms
3. Creates release packages when tags are pushed

## Troubleshooting

### .NET SDK Not Found
Install .NET 6.0 SDK from: https://dotnet.microsoft.com/download/dotnet/6.0

### Node.js/Yarn Not Found
- Install Node.js 18+: https://nodejs.org/
- Install Yarn: `npm install -g yarn`

### Lidarr Build Fails
1. Ensure Node.js and Yarn are installed
2. Check submodule is initialized: `git submodule update --init --recursive`
3. Try building manually: `cd ext/Lidarr && yarn install && ./build.sh --backend`

### Missing Dependencies
Run `dotnet restore` to download all NuGet packages

### Build Warnings
The project suppresses several warnings for Lidarr compatibility. This is normal and expected.