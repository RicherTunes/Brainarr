#!/bin/bash
set -e

echo "Setting up Lidarr dependencies for CI..."

# Create the target directory
mkdir -p mock-lidarr/bin

# Try to build assembly stubs first (preferred approach)
if [ -d "ci-stubs" ]; then
    echo "Building Lidarr assembly stubs..."
    cd ci-stubs
    
    # Build the stub assemblies
    dotnet build Lidarr.Core.Stubs/Lidarr.Core.Stubs.csproj -c Release -o ../mock-lidarr/bin
    CORE_RESULT=$?
    dotnet build Lidarr.Common.Stubs/Lidarr.Common.Stubs.csproj -c Release -o ../mock-lidarr/bin
    COMMON_RESULT=$?
    
    if [ $CORE_RESULT -eq 0 ] && [ $COMMON_RESULT -eq 0 ]; then
        echo "✅ Successfully built Lidarr assembly stubs"
        cd ..
        
        # Set environment variable
        if [ -n "$GITHUB_ENV" ]; then
            echo "LIDARR_PATH=$(pwd)/mock-lidarr/bin" >> $GITHUB_ENV
        else
            export LIDARR_PATH="$(pwd)/mock-lidarr/bin"
            echo "LIDARR_PATH set to: $LIDARR_PATH"
        fi
        exit 0
    else
        echo "⚠️ Failed to build assembly stubs (Core: $CORE_RESULT, Common: $COMMON_RESULT), falling back..."
        cd ..
    fi
fi

# Fallback: Try to use actual Lidarr assemblies if available
if [ -d "lidarr/_output/net6.0" ]; then
    echo "Using actual Lidarr assemblies..."
    cp -r lidarr/_output/net6.0/* mock-lidarr/bin/
    echo "LIDARR_PATH=$(pwd)/mock-lidarr/bin" >> $GITHUB_ENV
    exit 0
fi

# Last resort: Create minimal placeholder assemblies
echo "Creating minimal placeholder assemblies..."
cat > mock-lidarr/bin/Lidarr.Core.dll << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<assembly></assembly>
EOF

cat > mock-lidarr/bin/Lidarr.Common.dll << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<assembly></assembly>
EOF

cat > mock-lidarr/bin/Lidarr.Api.V1.dll << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<assembly></assembly>
EOF

cat > mock-lidarr/bin/Lidarr.Http.dll << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<assembly></assembly>
EOF

echo "LIDARR_PATH=$(pwd)/mock-lidarr/bin" >> $GITHUB_ENV
echo "⚠️ Using minimal placeholder assemblies - some builds may fail"