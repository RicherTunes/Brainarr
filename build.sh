#!/bin/bash

# Build script for Brainarr plugin
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Parse arguments
SETUP=false
TEST=false
PACKAGE=false
CLEAN=false
DEPLOY=false
CONFIGURATION="Release"
DEPLOY_PATH="X:/lidarr-hotio-test2/plugins/RicherTunes/Brainarr"

while [[ $# -gt 0 ]]; do
    case $1 in
        --setup)
            SETUP=true
            shift
            ;;
        --test)
            TEST=true
            shift
            ;;
        --package)
            PACKAGE=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --deploy)
            DEPLOY=true
            shift
            ;;
        --deploy-path)
            DEPLOY_PATH="$2"
            shift 2
            ;;
        --debug)
            CONFIGURATION="Debug"
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: ./build.sh [--setup] [--test] [--package] [--clean] [--deploy] [--deploy-path PATH] [--debug]"
            exit 1
            ;;
    esac
done

# Setup Lidarr if requested
if [ "$SETUP" = true ]; then
    echo -e "${GREEN}Setting up Lidarr source...${NC}"

    mkdir -p ext

    if [ ! -d "ext/Lidarr" ]; then
        echo -e "${YELLOW}Cloning Lidarr repository...${NC}"
        git clone --branch plugins --depth 1 https://github.com/Lidarr/Lidarr.git ext/Lidarr
    else
        echo -e "${YELLOW}Updating Lidarr repository...${NC}"
        cd ext/Lidarr
        git fetch origin plugins
        git reset --hard origin/plugins
        cd ../..
    fi

    echo -e "${YELLOW}Building Lidarr...${NC}"
    cd ext/Lidarr
    dotnet restore
    dotnet build -c Release
    cd ../..

    echo -e "${GREEN}Lidarr setup complete!${NC}"
fi

# Find Lidarr
LIDARR_FOUND=false
LIDARR_PATHS=(
    "./ext/Lidarr-docker/_output/net6.0"
    "./ext/Lidarr/_output/net6.0"
    "./ext/Lidarr/src/Lidarr/bin/Release/net6.0"
    "$LIDARR_PATH"
    "/opt/Lidarr"
    "/usr/lib/lidarr/bin"
)

for path in "${LIDARR_PATHS[@]}"; do
    if [ -n "$path" ] && [ -f "$path/Lidarr.Core.dll" ]; then
        echo -e "${GREEN}Found Lidarr at: $path${NC}"
        export LIDARR_PATH="$path"
        LIDARR_FOUND=true
        break
    fi
done

if [ "$LIDARR_FOUND" = false ]; then
    echo -e "${RED}Lidarr not found! Run with --setup to clone and build Lidarr.${NC}"
    echo -e "${YELLOW}Example: ./build.sh --setup${NC}"
    exit 1
fi

# Convert to absolute path for build process
export LIDARR_PATH=$(realpath "$LIDARR_PATH")

# Clean if requested
if [ "$CLEAN" = true ]; then
    echo -e "\n${GREEN}Cleaning build artifacts...${NC}"

    # Clean bin and obj directories
    CLEAN_PATHS=(
        "./Brainarr.Plugin/bin"
        "./Brainarr.Plugin/obj"
        "./Brainarr.Tests/bin"
        "./Brainarr.Tests/obj"
    )

    for path in "${CLEAN_PATHS[@]}"; do
        if [ -d "$path" ]; then
            echo -e "${YELLOW}Removing: $path${NC}"
            rm -rf "$path"
        fi
    done

    # Clean any existing packages
    for package in Brainarr-v*.zip; do
        if [ -f "$package" ]; then
            echo -e "${YELLOW}Removing package: $package${NC}"
            rm -f "$package"
        fi
    done

    echo -e "${GREEN}Clean completed!${NC}"
fi

# Build the plugin
echo -e "\n${GREEN}Building Brainarr plugin ($CONFIGURATION)...${NC}"
echo -e "${YELLOW}Using Lidarr assemblies from: $LIDARR_PATH${NC}"

cd Brainarr.Plugin
dotnet restore
# Build with explicit Lidarr path parameter
dotnet build -c "$CONFIGURATION" -p:LidarrPath="$LIDARR_PATH"
cd ..

echo -e "${GREEN}Build successful!${NC}"

# Run tests if requested
if [ "$TEST" = true ]; then
    echo -e "\n${GREEN}Running tests...${NC}"
    if [ -d "Brainarr.Tests" ]; then
        cd Brainarr.Tests
        dotnet test -c "$CONFIGURATION" --no-build
        cd ..
        echo -e "${GREEN}Tests passed!${NC}"
    else
        echo -e "${YELLOW}No test project found${NC}"
    fi
fi

# Package if requested
if [ "$PACKAGE" = true ]; then
    echo -e "\n${GREEN}Packaging plugin...${NC}"

    OUTPUT_PATH="./Brainarr.Plugin/bin"
    if [ ! -d "$OUTPUT_PATH" ]; then
        echo -e "${RED}Build output not found at: $OUTPUT_PATH${NC}"
        exit 1
    fi
    # Resolve version from root plugin.json without jq
    if [ -f "plugin.json" ]; then
        VERSION=$(grep -oE '"version"\s*:\s*"[^"]+"' plugin.json | head -1 | sed 's/.*:"\([^"]*\)"/\1/')
    else
        echo -e "${RED}plugin.json not found at repo root${NC}"
        exit 1
    fi
    if [ -z "$VERSION" ]; then
        echo -e "${RED}Failed to parse version from plugin.json${NC}"
        exit 1
    fi
    PACKAGE_NAME="Brainarr-$VERSION.net6.0.zip"

    # Remove existing package
    [ -f "$PACKAGE_NAME" ] && rm "$PACKAGE_NAME"

    # Create package (exclude Lidarr and debug files)
    cd "$OUTPUT_PATH"
    zip -r "../../$PACKAGE_NAME" Lidarr.Plugin.Brainarr.dll plugin.json -x "*.pdb"
    cd ../..

    echo -e "${GREEN}Package created: $PACKAGE_NAME${NC}"
fi

# Deploy if requested
if [ "$DEPLOY" = true ]; then
    echo -e "\n${GREEN}Deploying plugin...${NC}"

    # Check if deploy path exists, create if not
    if [ ! -d "$DEPLOY_PATH" ]; then
        echo -e "${YELLOW}Creating deploy directory: $DEPLOY_PATH${NC}"
        mkdir -p "$DEPLOY_PATH"
    fi

    # Check if we have built plugin files
    PLUGIN_DLL="./Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll"
    PLUGIN_JSON="./Brainarr.Plugin/bin/plugin.json"

    if [ ! -f "$PLUGIN_DLL" ]; then
        echo -e "${RED}Plugin DLL not found! Run build first.${NC}"
        exit 1
    fi

    if [ ! -f "$PLUGIN_JSON" ]; then
        echo -e "${RED}Plugin manifest not found! Run build first.${NC}"
        exit 1
    fi

    # Copy plugin files to deploy directory
    echo -e "${YELLOW}Copying plugin files to: $DEPLOY_PATH${NC}"

    # Copy main plugin DLL
    cp "$PLUGIN_DLL" "$DEPLOY_PATH/"
    echo -e "${GREEN}  Copied: Lidarr.Plugin.Brainarr.dll${NC}"

    # Copy plugin manifest
    cp "$PLUGIN_JSON" "$DEPLOY_PATH/"
    echo -e "${GREEN}  Copied: plugin.json${NC}"

    # Copy debug symbols if available
    PLUGIN_PDB="./Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.pdb"
    if [ -f "$PLUGIN_PDB" ]; then
        cp "$PLUGIN_PDB" "$DEPLOY_PATH/"
        echo -e "${GREEN}  Copied: Lidarr.Plugin.Brainarr.pdb (debug symbols)${NC}"
    fi

    echo -e "\n${GREEN}Deployment completed to: $DEPLOY_PATH${NC}"
    echo -e "${YELLOW}Restart Lidarr to load the updated plugin.${NC}"
fi

echo -e "\n${GREEN}Done!${NC}"
