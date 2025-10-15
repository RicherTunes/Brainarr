#!/usr/bin/env bash
set -euo pipefail

# Brainarr CI helper: verify extracted Lidarr assemblies and provenance
# - Confirms MANIFEST.txt exists and indicates Docker (no tarball fallback)
# - Validates Docker tag or digest against env (LIDARR_DOCKER_VERSION or LIDARR_DOCKER_DIGEST)
# - Ensures key assemblies exist
# - Exposes LIDARR_PATH for subsequent steps

OUT_DIR="ext/Lidarr-docker/_output/net6.0"
REQUIRE_DOCKER_ONLY=true

usage() {
  cat <<EOF
Usage: $0 [--output-dir DIR] [--allow-tar-fallback]

Options:
  --output-dir DIR        Directory containing extracted assemblies (default: ${OUT_DIR})
  --allow-tar-fallback    Permit 'Fallback: tarball' in MANIFEST (default: disallow)
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-dir) OUT_DIR="$2"; shift 2 ;;
    --allow-tar-fallback) REQUIRE_DOCKER_ONLY=false; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

MAN="$OUT_DIR/MANIFEST.txt"
test -f "$MAN" || { echo "MANIFEST missing at $MAN" >&2; exit 1; }

echo "=== Assemblies MANIFEST ==="
sed -n '1,120p' "$MAN" || true

# Enforce Docker provenance unless explicitly allowed to fallback
if $REQUIRE_DOCKER_ONLY; then
  if grep -q '^Fallback:\s*tarball\b' "$MAN"; then
    echo "Assemblies came from tarball fallback; Docker provenance required." >&2
    exit 1
  fi
fi

# Validate tag/digest consistency
EXPECT_TAG="ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION:-pr-plugins-2.14.2.4786}"
if [[ -n "${LIDARR_DOCKER_DIGEST:-}" ]]; then
  # When digest is provided, require exact digest in MANIFEST
  if ! grep -q "^DockerDigestEnv: ${LIDARR_DOCKER_DIGEST}\b" "$MAN"; then
    echo "Manifest digest mismatch (expected env digest ${LIDARR_DOCKER_DIGEST})." >&2
    exit 1
  fi
else
  # Otherwise, require the tag to match
  if ! grep -q "^DockerTag: ${EXPECT_TAG}\b" "$MAN"; then
    echo "Manifest tag mismatch (expected ${EXPECT_TAG})." >&2
    exit 1
  fi
fi

# Ensure required assemblies exist
REQ=(
  Lidarr.Core.dll
  Lidarr.dll
  Lidarr.Common.dll
)
for f in "${REQ[@]}"; do
  test -f "$OUT_DIR/$f" || { echo "Missing required assembly: $f" >&2; exit 1; }
done

# Sanity: at least one Lidarr.*.dll beyond Core
shopt -s nullglob
matches=("$OUT_DIR"/Lidarr.*.dll)
if (( ${#matches[@]} == 0 )); then
  echo "No Lidarr.*.dll assemblies found; extraction likely incomplete." >&2
  exit 1
fi

# Export LIDARR_PATH for callers
echo "LIDARR assemblies verified in: $OUT_DIR"
if [[ -n "${GITHUB_ENV:-}" && -w "${GITHUB_ENV:-}" ]]; then
  echo "LIDARR_PATH=$OUT_DIR" >> "$GITHUB_ENV"
  echo "Exported LIDARR_PATH via GITHUB_ENV"
else
  echo "export LIDARR_PATH=\"$OUT_DIR\""
fi

echo "check-assemblies.sh: OK"
