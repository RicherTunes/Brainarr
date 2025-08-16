#!/bin/bash
# Download Lidarr DLLs for plugin development

set -e

LIDARR_VERSION="${1:-2.12.4.4658}"
TARGET_PATH="${2:-lidarr-dlls}"

echo "Setting up Lidarr DLLs for plugin development..."

# Create target directory
if [ -d "$TARGET_PATH" ]; then
    echo "Cleaning existing Lidarr DLLs directory..."
    rm -rf "$TARGET_PATH"
fi
mkdir -p "$TARGET_PATH"

# Download Lidarr release
DOWNLOAD_URL="https://github.com/Lidarr/Lidarr/releases/download/v$LIDARR_VERSION/Lidarr.master.$LIDARR_VERSION.linux-core-x64.tar.gz"
DOWNLOAD_PATH="lidarr-temp.tar.gz"

echo "Downloading Lidarr v$LIDARR_VERSION..."
curl -L -o "$DOWNLOAD_PATH" "$DOWNLOAD_URL" || {
    echo "Failed to download Lidarr release. Please check the version number or try a different version."
    exit 1
}

# Extract only the required DLLs
echo "Extracting required DLLs..."
mkdir -p temp-extract
tar -xzf "$DOWNLOAD_PATH" -C temp-extract

# Copy required DLLs
REQUIRED_DLLS=(
    "Lidarr.Core.dll"
    "Lidarr.Common.dll"
    "Lidarr.Api.V1.dll" 
    "Lidarr.Http.dll"
)

for dll in "${REQUIRED_DLLS[@]}"; do
    SOURCE_PATH="temp-extract/Lidarr/$dll"
    if [ -f "$SOURCE_PATH" ]; then
        cp "$SOURCE_PATH" "$TARGET_PATH/"
        echo "  ✓ $dll"
    else
        echo "  ✗ $dll not found in release" >&2
    fi
done

# Cleanup
rm -f "$DOWNLOAD_PATH"
rm -rf temp-extract

echo "Lidarr DLLs setup complete in $TARGET_PATH/"
echo "Set LIDARR_PATH environment variable to: $(realpath $TARGET_PATH)"