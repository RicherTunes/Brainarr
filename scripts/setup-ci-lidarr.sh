#!/bin/bash
set -e

echo "Setting up Lidarr dependencies for CI..."

# Create the target directory
mkdir -p mock-lidarr/bin

# Try to build assembly stubs first (preferred approach)
if [ -d "ci-stubs" ]; then
    echo "Building Lidarr assembly stubs..."
    cd ci-stubs
    
    # Build the stub assembly
    dotnet build Lidarr.Core.Stubs.csproj -c Release -o ../mock-lidarr/bin
    
    if [ $? -eq 0 ]; then
        echo "✅ Successfully built Lidarr assembly stubs"
        cd ..
        
        # Set environment variable
        echo "LIDARR_PATH=$(pwd)/mock-lidarr/bin" >> $GITHUB_ENV
        exit 0
    else
        echo "⚠️ Failed to build assembly stubs, falling back..."
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