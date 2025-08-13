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
CONFIGURATION="Release"

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
        --debug)
            CONFIGURATION="Debug"
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: ./build.sh [--setup] [--test] [--package] [--debug]"
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

# Build the plugin
echo -e "\n${GREEN}Building Brainarr plugin ($CONFIGURATION)...${NC}"
cd Brainarr.Plugin
dotnet restore
dotnet build -c "$CONFIGURATION"
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
    
    OUTPUT_PATH="./Brainarr.Plugin/bin/$CONFIGURATION/net6.0"
    if [ ! -d "$OUTPUT_PATH" ]; then
        echo -e "${RED}Build output not found at: $OUTPUT_PATH${NC}"
        exit 1
    fi
    
    VERSION="1.0.0"
    PACKAGE_NAME="Brainarr-v$VERSION.zip"
    
    # Remove existing package
    [ -f "$PACKAGE_NAME" ] && rm "$PACKAGE_NAME"
    
    # Create package (exclude Lidarr and debug files)
    cd "$OUTPUT_PATH"
    zip -r "../../../../$PACKAGE_NAME" . -x "*.pdb" -x "Lidarr.*" -x "NzbDrone.*"
    cd ../../../..
    
    echo -e "${GREEN}Package created: $PACKAGE_NAME${NC}"
fi

echo -e "\n${GREEN}Done!${NC}"