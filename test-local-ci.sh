#!/bin/bash
# Local CI Testing Script (Bash version)
# This script mimics the GitHub Actions CI environment locally

set -e  # Exit on any error

DOTNET_VERSION=${1:-"6.0.x"}
SKIP_DOWNLOAD=${2:-false}
VERBOSE=${3:-false}

echo "?? Starting Local CI Test (mimicking GitHub Actions)"
echo "Target .NET Version: $DOTNET_VERSION"

# Step 1: Clean up any previous runs
echo ""
echo "?? Cleaning previous artifacts..."
rm -f lidarr.tar.gz
rm -rf Lidarr/
rm -rf TestResults/

# Step 2: Ensure ext/Lidarr/_output/net6.0 directory exists
echo ""
echo "?? Setting up Lidarr assemblies directory..."
mkdir -p ext/Lidarr/_output/net6.0

# Step 3: Obtain Lidarr assemblies (prefer plugins Docker, fallback to tar.gz)
if [ "$SKIP_DOWNLOAD" != "true" ]; then
    echo ""; echo "?? Obtaining Lidarr assemblies..."
    LIDARR_DOCKER_VERSION=${LIDARR_DOCKER_VERSION:-"pr-plugins-2.13.3.4692"}
    if command -v docker >/dev/null 2>&1; then
        echo "Using Docker image: ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}"
        docker pull ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}
        cid=$(docker create ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION})
        # Copy entire /app/bin to ensure all runtime dependencies are present (e.g., Equ.dll)
        docker cp "$cid:/app/bin/." ext/Lidarr/_output/net6.0/
        docker rm -f "$cid" >/dev/null
        echo "? Assemblies ready (Docker)"; ls -la ext/Lidarr/_output/net6.0/ || true
    else
        echo "Docker not found; falling back to release tarball"
        LIDARR_URL=$(curl -s https://api.github.com/repos/Lidarr/Lidarr/releases/latest | grep "browser_download_url.*linux-core-x64.tar.gz" | cut -d '"' -f 4 | head -1)
        if [ -z "$LIDARR_URL" ]; then
            LIDARR_URL="https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz"
        fi
        echo "Downloading from: $LIDARR_URL"
        curl -L "$LIDARR_URL" -o lidarr.tar.gz
        tar -xzf lidarr.tar.gz
        if [ -d "Lidarr" ]; then
            for f in Lidarr.Core.dll Lidarr.Common.dll Lidarr.Http.dll Lidarr.Api.V1.dll; do
                cp "Lidarr/$f" ext/Lidarr/_output/net6.0/ 2>/dev/null || echo "$f not found"
            done
            echo "? Assemblies ready (tar.gz)"; ls -la ext/Lidarr/_output/net6.0/ || true
        else
            echo "? Failed to extract Lidarr archive"; exit 1
        fi
    fi
else
    echo "?? Skipping download (using existing assemblies)"
fi

# Step 4: Set environment variable (same as CI)
export LIDARR_PATH="$(pwd)/ext/Lidarr/_output/net6.0"
echo ""; echo "?? Set LIDARR_PATH=$LIDARR_PATH"

# Step 5: Restore dependencies
echo ""; echo "?? Restoring dependencies..."
dotnet restore Brainarr.sln

# Step 6: Build (same as CI)
echo ""; echo "?? Building solution..."
BUILD_ARGS="build Brainarr.sln --no-restore --configuration Release -p:LidarrPath=$LIDARR_PATH"
if [ "$VERBOSE" = "true" ]; then
    BUILD_ARGS="$BUILD_ARGS --verbosity detailed"
fi
dotnet $BUILD_ARGS

# Step 7: Run tests (same as CI)
echo ""; echo "?? Running tests..."
mkdir -p TestResults
dotnet test Brainarr.sln --no-build --configuration Release \
    --verbosity normal \
    --collect:"XPlat Code Coverage" \
    --logger "trx;LogFileName=test-results.trx" \
    --results-directory TestResults/ \
    --blame-hang-timeout 5m

TEST_EXIT_CODE=$?

# Step 8: Show results
echo ""; echo "?? Test Results:"
if [ -d "TestResults" ]; then
    find TestResults -type f -name "*.trx" -o -name "*.xml" | head -10
fi
if [ $TEST_EXIT_CODE -eq 0 ]; then
    echo "? All tests passed!"
else
    echo "?? Some tests failed (exit code: $TEST_EXIT_CODE)"
fi

echo ""; echo "?? Local CI test complete!"; echo "This environment now matches your GitHub Actions CI setup."
exit $TEST_EXIT_CODE
