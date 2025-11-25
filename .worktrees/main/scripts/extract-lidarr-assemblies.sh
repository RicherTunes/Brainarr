#!/usr/bin/env bash
set -euo pipefail

# Brainarr CI helper: Extract Lidarr assemblies from the plugins image
# Fallback order: digest (if provided) -> tag -> pinned tar.gz

OUT_DIR="ext/Lidarr-docker/_output/net6.0"
NO_TAR_FALLBACK="false"
MODE="minimal" # minimal|full

while [[ $# -gt 0 ]]; do
  case "$1" in
    --output-dir)
      OUT_DIR="$2"; shift 2 ;;
    --no-tar-fallback)
      NO_TAR_FALLBACK="true"; shift ;;
    --mode)
      MODE="$2"; shift 2 ;;
    *)
      echo "Unknown argument: $1"; exit 2 ;;
  esac
done

mkdir -p "$OUT_DIR"

LIDARR_DOCKER_VERSION="${LIDARR_DOCKER_VERSION:-pr-plugins-2.14.1.4716}"
LIDARR_DOCKER_DIGEST="${LIDARR_DOCKER_DIGEST:-}"

TAG_IMAGE="ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}"
IMAGE="$TAG_IMAGE"

DOCKER_OK=true

# Retry helper for flaky docker pulls
docker_pull_retry() {
  local image="$1"
  local attempts=${2:-3}
  local delay=${3:-3}
  local n=1
  until [ $n -gt $attempts ]; do
    if docker pull "$image" >/dev/null; then
      return 0
    fi
    echo "docker pull failed for $image (attempt $n/$attempts), retrying in ${delay}s..."
    sleep "$delay"
    n=$((n+1))
  done
  return 1
}
if [[ -n "$LIDARR_DOCKER_DIGEST" ]]; then
  DIGEST_IMAGE="ghcr.io/hotio/lidarr@${LIDARR_DOCKER_DIGEST}"
  echo "Attempting Docker image (digest): ${DIGEST_IMAGE}"
  if docker_pull_retry "$DIGEST_IMAGE"; then
    IMAGE="$DIGEST_IMAGE"
  else
    echo "Digest pull failed; attempting tag: ${TAG_IMAGE}"
    if ! docker_pull_retry "$TAG_IMAGE"; then
      echo "Tag pull failed as well; will use tar.gz fallback."
      DOCKER_OK=false
    fi
  fi
else
  echo "Using Docker image (tag): ${TAG_IMAGE}"
  if ! docker_pull_retry "$TAG_IMAGE"; then
    echo "Tag pull failed; will use tar.gz fallback."
    DOCKER_OK=false
  fi
fi

CONTAINER_CREATED=false
if [[ "$DOCKER_OK" == true ]]; then
  # Create container; retry with tag if needed
  if ! docker create --name temp-lidarr "$IMAGE" >/dev/null 2>&1; then
    echo "Failed to create container from ${IMAGE}, retrying with ${TAG_IMAGE}"
    if docker create --name temp-lidarr "$TAG_IMAGE" >/dev/null 2>&1; then
      CONTAINER_CREATED=true
    fi
  else
    CONTAINER_CREATED=true
  fi
fi

copy_from_container() {
  local file="$1"
  docker cp temp-lidarr:/app/bin/${file} "$OUT_DIR/" 2>/dev/null || return 1
}

FALLBACK_USED="none"

if [[ "$CONTAINER_CREATED" == true && "$MODE" == "full" ]]; then
  echo "Mode=full: copying entire /app/bin"
  docker cp temp-lidarr:/app/bin/. "$OUT_DIR/"
elif [[ "$CONTAINER_CREATED" == true ]]; then
  echo "Mode=minimal: copying required Lidarr and Microsoft.Extensions assemblies"
  # Required core assemblies
  REQ=(
    Lidarr.dll
    Lidarr.Common.dll
    Lidarr.Core.dll
    Lidarr.Http.dll
    Lidarr.Api.V1.dll
    Lidarr.Host.dll
  )

  for f in "${REQ[@]}"; do
    copy_from_container "$f" || echo "Optional assembly missing: $f"
  done

  # Opportunistic Microsoft.Extensions assemblies
  OPT=(
    Microsoft.Extensions.Caching.Memory.dll
    Microsoft.Extensions.Caching.Abstractions.dll
    Microsoft.Extensions.DependencyInjection.Abstractions.dll
    Microsoft.Extensions.Logging.Abstractions.dll
    Microsoft.Extensions.Options.dll
    Microsoft.Extensions.Primitives.dll
  )

  for f in "${OPT[@]}"; do
    copy_from_container "$f" || true
  done
fi

if [[ "$CONTAINER_CREATED" == true ]]; then
  docker rm -f temp-lidarr >/dev/null || true
fi

# Tar.gz fallback if Docker-based copy did not produce core assembly
if [[ ! -f "$OUT_DIR/Lidarr.Core.dll" ]]; then
  if [[ "$NO_TAR_FALLBACK" == "true" ]]; then
    echo "No tar.gz fallback requested and Lidarr.Core.dll missing" >&2
    exit 1
  fi
  echo "Docker-based extraction failed; attempting pinned tar.gz fallback..."
  # Best-effort parse of version from pr-plugins-<ver>
  CANDIDATES=()
  VNUM="${LIDARR_DOCKER_VERSION#pr-plugins-}"
  if [[ "$VNUM" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    CANDIDATES+=("$VNUM")
  fi
  # Always try known-good as secondary (must exist as a GitHub Release asset)
  CANDIDATES+=("2.13.3.4711")

  TAR_OK=false
  for VER in "${CANDIDATES[@]}"; do
    echo "Trying Lidarr tarball version: $VER"
    URL="https://github.com/Lidarr/Lidarr/releases/download/v${VER}/Lidarr.master.${VER}.linux-core-x64.tar.gz"
    if curl -fsSL --retry 3 "$URL" -o lidarr.tar.gz; then
      if tar -xzf lidarr.tar.gz; then
        TAR_OK=true
        break
      fi
    else
      echo "Tarball not available for version $VER at $URL"
    fi
  done

  if [[ "$TAR_OK" == true && -d Lidarr ]]; then
    if [[ "$MODE" == "full" ]]; then
      cp -r Lidarr/. "$OUT_DIR/"
    else
      for f in "${REQ[@]}"; do
        [[ -f "Lidarr/$f" ]] && cp "Lidarr/$f" "$OUT_DIR/" || echo "Missing $f (optional)"
      done
    fi
    rm -f lidarr.tar.gz
    FALLBACK_USED="tarball"
  else
    echo "All tarball fallbacks failed" >&2
    exit 1
  fi
fi

# Sanity
test -f "$OUT_DIR/Lidarr.Core.dll" || { echo "Missing Lidarr.Core.dll after extraction" >&2; exit 1; }
echo "Final assemblies in: $OUT_DIR"
ls -la "$OUT_DIR" || true

### Provenance manifest
# Figure out which image was actually used (if any)
if [[ "$CONTAINER_CREATED" == true ]]; then
  USED_IMAGE="$IMAGE"
else
  USED_IMAGE="none"
fi

# Resolve digest from the image we used when available, else from the tag
RESOLVED_FROM="$USED_IMAGE"
if [[ "$RESOLVED_FROM" == "none" || -z "$RESOLVED_FROM" ]]; then
  RESOLVED_FROM="$TAG_IMAGE"
fi
RESOLVED_DIGEST=$(docker image inspect --format '{{index .RepoDigests 0}}' "$RESOLVED_FROM" 2>/dev/null || true)
{
  echo "Brainarr Assemblies Manifest"
  echo "Date: $(date -u +'%Y-%m-%dT%H:%M:%SZ')"
  echo "Mode: $MODE"
  echo "OutputDir: $OUT_DIR"
  echo "UsedImage: $USED_IMAGE"
  echo "DockerTag: $TAG_IMAGE"
  echo "DockerDigestEnv: ${LIDARR_DOCKER_DIGEST:-}"
  echo "ResolvedDigest: ${RESOLVED_DIGEST}"
  echo "Fallback: ${FALLBACK_USED}"
  echo "Files:"
  ls -1 "$OUT_DIR" | sed 's/^/  - /'
} > "$OUT_DIR/MANIFEST.txt"
