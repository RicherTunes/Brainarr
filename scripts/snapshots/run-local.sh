#!/usr/bin/env bash
set -euo pipefail

# Brainarr UI screenshots – local runner
# Builds the plugin, starts Lidarr (plugins) in Docker with Brainarr mounted,
# then drives the UI with Playwright to capture PNGs under docs/assets/screenshots/.

PORT="8686"
LIDARR_TAG="pr-plugins-3.1.2.4913"
CONTAINER_NAME="lidarr-ss"
SKIP_BUILD="false"
WAIT_SECS="600" # 10 minutes max

usage() {
  cat <<EOF
Usage: $0 [--port 8686] [--lidarr-tag ${LIDARR_TAG}] [--skip-build] [--wait-secs 600]

Examples:
  $0                      # build + run at http://localhost:8686
  $0 --port 8765         # run at http://localhost:8765
  $0 --skip-build        # assume plugin already built/staged
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2;;
    --lidarr-tag) LIDARR_TAG="$2"; shift 2;;
    --skip-build) SKIP_BUILD="true"; shift;;
    --wait-secs) WAIT_SECS="$2"; shift 2;;
    -h|--help) usage; exit 0;;
    *) echo "Unknown arg: $1"; usage; exit 1;;
  esac
done

need() { command -v "$1" >/dev/null 2>&1 || { echo "Missing dependency: $1" >&2; exit 1; }; }
need docker
need dotnet
need node
npm -v >/dev/null 2>&1 || { echo "Missing npm (Node tool)" >&2; exit 1; }

ROOT_DIR="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT_DIR"

# Ensure Lidarr assemblies exist; recommend setup if missing
if [[ ! -f ext/Lidarr/_output/net8.0/Lidarr.Core.dll ]]; then
  echo "Lidarr assemblies not found at ext/Lidarr/_output/net8.0; running ./setup.sh --setup" >&2
  chmod +x ./setup.sh 2>/dev/null || true
  ./setup.sh --setup || { echo "setup.sh failed" >&2; exit 1; }
fi

# Build Brainarr plugin (unless skipped)
if [[ "$SKIP_BUILD" != "true" ]]; then
  dotnet restore Brainarr.Plugin/Brainarr.Plugin.csproj
  dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -c Release -p:LidarrPath="$(pwd)/ext/Lidarr/_output/net8.0" -m:1
fi

# Stage plugin files for Lidarr plugin mount
mkdir -p plugin-dist
DLL_SRC=""
if [[ -f Brainarr.Plugin/bin/Release/net8.0/Lidarr.Plugin.Brainarr.dll ]]; then
  DLL_SRC="Brainarr.Plugin/bin/Release/net8.0/Lidarr.Plugin.Brainarr.dll"
elif [[ -f Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll ]]; then
  DLL_SRC="Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll"
fi
if [[ -z "$DLL_SRC" ]]; then
  echo "Could not find built plugin DLL. Ensure the project built successfully." >&2
  find Brainarr.Plugin -name 'Lidarr.Plugin.Brainarr.dll' -path '*/bin/*' | sed -n '1,10p' || true
  exit 1
fi
cp "$DLL_SRC" plugin-dist/
cp plugin.json manifest.json plugin-dist/
echo "Staged plugin from: $DLL_SRC"

# Start Lidarr (plugins branch) with plugin mounted
set +e
docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1
set -e
docker pull "ghcr.io/hotio/lidarr:${LIDARR_TAG}"
docker run -d --name "$CONTAINER_NAME" \
  -p "${PORT}:8686" \
  -v "$(pwd)/plugin-dist:/config/plugins/RicherTunes/Brainarr:ro" \
  -e PUID=1000 -e PGID=1000 \
  "ghcr.io/hotio/lidarr:${LIDARR_TAG}"

# Wait for UI
echo "Waiting for Lidarr UI on http://localhost:${PORT} ..."
deadline=$((SECONDS + WAIT_SECS))
until curl -fsS "http://localhost:${PORT}" >/dev/null 2>&1; do
  if (( SECONDS >= deadline )); then
    echo "Timeout waiting for Lidarr UI." >&2
    docker logs "$CONTAINER_NAME" --tail=200 || true
    exit 1
  fi
  sleep 5
done

# Install playwright + browsers (locally)
if ! node -e "require('playwright')" >/dev/null 2>&1; then
  npm i -D playwright@1.48.2
fi
npx playwright install chromium >/dev/null 2>&1 || true

# Run snapshots
export LIDARR_BASE_URL="http://localhost:${PORT}"
node scripts/snapshots/snap.mjs

echo "Screenshots saved under docs/assets/screenshots/"
echo "Stopping container..."
docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

echo "Done."
