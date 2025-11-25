#!/bin/bash
set -euo pipefail

# Simplified NuGet.config patching - based on Tubifarry's working approach
# Just adds missing Azure DevOps feeds without complex packageSourceMapping

#######################################
# Patch ext/Lidarr/src/NuGet.config
#######################################
LIDARR_CFG="ext/Lidarr/src/NuGet.config"

if [ ! -f "$LIDARR_CFG" ]; then
  echo "Lidarr NuGet.config not found at $LIDARR_CFG - skipping"
else
  echo "Patching $LIDARR_CFG..."

  # Check if SQLite feed already exists
  if grep -q 'SQLite' "$LIDARR_CFG"; then
    echo "Lidarr config already has SQLite feed"
  else
    # Simple approach: replace the config with all needed feeds
    cat > "$LIDARR_CFG" << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="Taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
    <add key="SQLite" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/SQLite/nuget/v3/index.json" />
    <add key="FluentMigrator" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/FluentMigrator/nuget/v3/index.json" />
    <add key="dotnet-bsd-crossbuild" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/dotnet-bsd-crossbuild/nuget/v3/index.json" />
    <add key="Mono.Posix.NETStandard" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/Mono.Posix.NETStandard/nuget/v3/index.json" />
  </packageSources>
</configuration>
EOF
    echo "Lidarr NuGet.config patched"
  fi
fi

#######################################
# Patch ext/lidarr.plugin.common/NuGet.config
#######################################
COMMON_CFG="ext/lidarr.plugin.common/NuGet.config"

if [ ! -f "$COMMON_CFG" ]; then
  echo "Plugin common NuGet.config not found at $COMMON_CFG - skipping"
else
  echo "Patching $COMMON_CFG..."

  # Check if Taglib feed already exists
  if grep -q 'Taglib' "$COMMON_CFG"; then
    echo "Plugin common config already has Taglib feed"
  else
    # Simple approach: replace with all needed feeds, no packageSourceMapping
    cat > "$COMMON_CFG" << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="Taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
    <add key="SQLite" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/SQLite/nuget/v3/index.json" />
    <add key="FluentMigrator" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/FluentMigrator/nuget/v3/index.json" />
    <add key="dotnet-bsd-crossbuild" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/dotnet-bsd-crossbuild/nuget/v3/index.json" />
    <add key="Mono.Posix.NETStandard" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/Mono.Posix.NETStandard/nuget/v3/index.json" />
  </packageSources>
</configuration>
EOF
    echo "Plugin common NuGet.config patched"
  fi
fi

echo "NuGet config patching complete"
