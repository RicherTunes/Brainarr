#!/bin/bash
set -e

echo "=== Setting up Lidarr dependencies for Brainarr plugin ==="

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK 6.0 is required but not installed."
    echo "Please install it from: https://dotnet.microsoft.com/download/dotnet/6.0"
    echo ""
    echo "For Ubuntu/Debian:"
    echo "  wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb"
    echo "  sudo dpkg -i packages-microsoft-prod.deb"
    echo "  sudo apt-get update && sudo apt-get install -y dotnet-sdk-6.0"
    echo ""
    echo "For macOS:"
    echo "  brew install --cask dotnet-sdk"
    exit 1
fi

echo "Found .NET SDK: $(dotnet --version)"

echo "Building Lidarr from source..."
cd ext/Lidarr

# Clean any previous builds
rm -rf _output _tests

# Build Lidarr backend
echo "Running Lidarr build script (this may take a few minutes)..."
./build.sh --backend

echo "=== Build complete ==="
echo "Lidarr binaries are available in ext/Lidarr/_output/"

# Go back to repo root
cd ../..

# Check if build was successful
LIDARR_OUTPUT_PATH="ext/Lidarr/_output/net6.0"

if [ -d "$LIDARR_OUTPUT_PATH" ]; then
    echo "Lidarr build successful! Binaries are in $LIDARR_OUTPUT_PATH"
    export LIDARR_PATH="$(pwd)/$LIDARR_OUTPUT_PATH"
    echo "Set LIDARR_PATH=$LIDARR_PATH"
    echo ""
    echo "To persist this setting, add to your shell profile:"
    echo "  export LIDARR_PATH=$(pwd)/$LIDARR_OUTPUT_PATH"
else
    echo "ERROR: Could not find Lidarr build output in $LIDARR_OUTPUT_PATH"
    exit 1
fi