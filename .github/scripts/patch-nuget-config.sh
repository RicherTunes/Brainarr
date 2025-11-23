#!/bin/bash
set -euo pipefail

# Patch lidarr.plugin.common NuGet.config to add Azure DevOps feeds
# This is needed when building Lidarr from source via ProjectReferences

CFG="ext/lidarr.plugin.common/NuGet.config"

if [ ! -f "$CFG" ]; then
  echo "NuGet.config not found at $CFG"
  exit 1
fi

echo "Patching $CFG to add Azure DevOps package sources and mappings..."

# Check if feeds are already added (idempotent)
if grep -q 'lidarr-taglib' "$CFG"; then
  echo "Feeds already configured, skipping patch"
  exit 0
fi

# Create the complete configuration with feeds and mappings
cat > "$CFG" << 'NUGET_EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="lidarr-taglib" value="https://pkgs.dev.azure.com/Lidarr/Lidarr/_packaging/Taglib/nuget/v3/index.json" />
    <add key="servarr-sqlite" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/SQLite/nuget/v3/index.json" />
    <add key="servarr-fluentmigrator" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/FluentMigrator/nuget/v3/index.json" />
    <add key="servarr-bsd-crossbuild" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/dotnet-bsd-crossbuild/nuget/v3/index.json" />
    <add key="servarr-mono-posix" value="https://pkgs.dev.azure.com/Servarr/Servarr/_packaging/Mono.Posix.NETStandard/nuget/v3/index.json" />
  </packageSources>

  <packageSourceMapping>
    <packageSource key="lidarr-taglib">
      <package pattern="TagLibSharp-Lidarr*" />
    </packageSource>
    <packageSource key="servarr-sqlite">
      <package pattern="Microsoft.Data.Sqlite*" />
      <package pattern="SQLitePCLRaw*" />
    </packageSource>
    <packageSource key="servarr-fluentmigrator">
      <package pattern="FluentMigrator*" />
    </packageSource>
    <packageSource key="servarr-bsd-crossbuild">
      <package pattern="runtime.freebsd*" />
      <package pattern="runtime.osx*" />
    </packageSource>
    <packageSource key="servarr-mono-posix">
      <package pattern="Mono.Posix.NETStandard*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="Microsoft.*" />
      <package pattern="System.*" />
      <package pattern="Newtonsoft.*" />
      <package pattern="NLog*" />
      <package pattern="FluentValidation*" />
      <package pattern="xunit*" />
      <package pattern="Moq*" />
      <package pattern="Castle.*" />
      <package pattern="dotnet-reportgenerator-globaltool" />
      <package pattern="dotnet-stryker" />
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
NUGET_EOF

echo "NuGet.config patched successfully"
