#!/bin/bash
set -e

# Test script to verify CI build setup works correctly
echo "Testing CI build setup..."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Clean up any existing mock directories
rm -rf "$PROJECT_ROOT/mock-lidarr"

# Run the CI setup script
echo "Running CI setup script..."
chmod +x "$PROJECT_ROOT/scripts/setup-ci-lidarr.sh"
"$PROJECT_ROOT/scripts/setup-ci-lidarr.sh"

# Verify assemblies were created
MOCK_LIDARR_DIR="$PROJECT_ROOT/mock-lidarr/bin"
echo ""
echo "Verifying assemblies..."

if [ -f "$MOCK_LIDARR_DIR/Lidarr.Core.dll" ]; then
    echo "✓ Lidarr.Core.dll found"
else
    echo "✗ Lidarr.Core.dll missing"
    exit 1
fi

if [ -f "$MOCK_LIDARR_DIR/Lidarr.Common.dll" ]; then
    echo "✓ Lidarr.Common.dll found"
else
    echo "✗ Lidarr.Common.dll missing"
    exit 1
fi

# Set environment variable
export LIDARR_PATH="$MOCK_LIDARR_DIR"

# Test building the plugin
echo ""
echo "Testing plugin build..."
cd "$PROJECT_ROOT"

# Install test dependencies
dotnet add Brainarr.Tests/Brainarr.Tests.csproj package Equ --version 2.3.0 > /dev/null 2>&1 || true

# Try to restore and build
echo "Restoring packages..."
if dotnet restore --verbosity quiet; then
    echo "✓ Package restore successful"
else
    echo "✗ Package restore failed"
    exit 1
fi

echo "Building plugin..."
if dotnet build --configuration Release --verbosity quiet --no-restore; then
    echo "✓ Plugin build successful"
else
    echo "✗ Plugin build failed"
    exit 1
fi

echo ""
echo "🎉 CI build test completed successfully!"
echo "   The assembly stubs and CI setup are working correctly."