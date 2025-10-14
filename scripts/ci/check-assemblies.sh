#!/usr/bin/env bash
set -euo pipefail

# Brainarr CI sanity: verify Lidarr assemblies are present and consistent
# Usage: check-assemblies.sh [assemblies_dir] [--expect-tag ghcr.io/hotio/lidarr:TAG]

DIR=${1:-"ext/Lidarr-docker/_output/net6.0"}
EXPECT_TAG=""

if [[ ${2:-} == "--expect-tag" ]]; then
  EXPECT_TAG=${3:-}
fi

echo "[check-assemblies] Checking directory: $DIR"
[[ -d "$DIR" ]] || { echo "[check-assemblies] Missing directory: $DIR" >&2; exit 1; }

need_files=(
  "Lidarr.Core.dll"
  "Lidarr.Common.dll"
  "Lidarr.Http.dll"
  "Lidarr.Api.V1.dll"
)

missing=0
for f in "${need_files[@]}"; do
  if [[ ! -f "$DIR/$f" ]]; then
    echo "[check-assemblies] Missing $f in $DIR" >&2
    missing=1
  fi
done

if [[ $missing -ne 0 ]]; then
  echo "[check-assemblies] One or more required assemblies are missing." >&2
  exit 1
fi

# Optional manifest consistency check
if [[ -n "$EXPECT_TAG" ]]; then
  MAN="$DIR/MANIFEST.txt"
  if [[ ! -f "$MAN" ]]; then
    echo "[check-assemblies] Missing MANIFEST.txt while expecting tag $EXPECT_TAG" >&2
    exit 1
  fi
  if ! grep -q "$EXPECT_TAG" "$MAN"; then
    echo "[check-assemblies] MANIFEST.txt does not contain expected tag: $EXPECT_TAG" >&2
    echo "--- MANIFEST.txt (first 20 lines) ---" >&2
    sed -n '1,20p' "$MAN" >&2 || true
    exit 1
  fi
fi

echo "[check-assemblies] OK: Assemblies present${EXPECT_TAG:+ and manifest matches expected tag}."
