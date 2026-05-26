#!/usr/bin/env bash
set -euo pipefail
# Brainarr shim — delegates to the shared superset in Lidarr.Plugin.Common.
# Default output dir matches brainarr CI: ext/Lidarr-docker/_output/net8.0
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$SCRIPT_DIR/../ext/Lidarr.Plugin.Common/scripts/extract-lidarr-assemblies.sh" \
  --output-dir "ext/Lidarr-docker/_output/net8.0" \
  --plugin-name "Brainarr" \
  "$@"
