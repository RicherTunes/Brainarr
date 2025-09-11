#!/bin/bash
# Lidarr Duplicate Cleanup - Linux/macOS Shell Wrapper
# Usage: ./cleanup-duplicates.sh [--dry-run] [--verbose]

echo "🎵 Lidarr Duplicate Artist Cleanup"
echo "================================"

# Check if Python is installed
if ! command -v python3 &> /dev/null; then
    echo "❌ Error: Python 3 not found. Please install Python 3.6+ from your package manager"
    echo "   Ubuntu/Debian: sudo apt install python3 python3-pip"
    echo "   macOS: brew install python3"
    exit 1
fi

# Check if requests library is installed
if ! python3 -c "import requests" &> /dev/null; then
    echo "❌ Error: 'requests' library not found. Installing..."
    if ! pip3 install requests; then
        echo "❌ Failed to install requests. Please run: pip3 install requests"
        exit 1
    fi
fi

# Configuration - EDIT THESE VALUES
LIDARR_URL="http://localhost:8686"
LIDARR_API_KEY="YOUR_API_KEY_HERE" # pragma: allowlist secret

# Check if user has configured the script
if [ "$LIDARR_API_KEY" = "YOUR_API_KEY_HERE" ]; then
    echo "❌ Please edit this script and set your LIDARR_URL and LIDARR_API_KEY"
    echo ""
    echo "   1. Edit cleanup-duplicates.sh with your favorite editor"
    echo "   2. Change LIDARR_URL to your Lidarr URL (e.g., http://localhost:8686)"
    echo "   3. Change LIDARR_API_KEY to your API key from Lidarr Settings > General"
    echo ""
    exit 1
fi

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Build command with arguments
PYTHON_CMD="python3 \"$SCRIPT_DIR/cleanup-duplicates.py\" --url \"$LIDARR_URL\" --api-key \"$LIDARR_API_KEY\""

# Add any command line arguments passed to this script
if [ $# -gt 0 ]; then
    PYTHON_CMD="$PYTHON_CMD $*"
else
    # Default to dry-run for safety
    PYTHON_CMD="$PYTHON_CMD --dry-run"
    echo "ℹ️  Running in DRY-RUN mode by default. Add --no-dry-run to actually remove duplicates."
    echo ""
fi

# Execute the Python script
echo "▶️  Running: $PYTHON_CMD"
echo ""
eval $PYTHON_CMD

# Show result
if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Script completed successfully!"
else
    echo ""
    echo "❌ Script completed with errors. Check lidarr-cleanup.log for details."
fi

echo ""
echo "📋 Log file: $SCRIPT_DIR/lidarr-cleanup.log"
