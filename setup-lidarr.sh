#!/usr/bin/env bash
# Portable Lidarr setup for plugin development
# Defaults to extracting assemblies from the Lidarr plugins Docker image.
# Optionally supports cloning/building Lidarr from the plugins branch.

set -euo pipefail

MODE="docker"        # docker | source
BRANCH="plugins"     # used when MODE=source
EXT_PATH="ext/Lidarr"
OUT_PATH="$EXT_PATH/_output/net8.0"
DOCKER_TAG="${LIDARR_DOCKER_VERSION:-pr-plugins-3.1.2.4913}"

usage() {
  cat <<EOF
Usage: $0 [--mode docker|source] [--branch plugins] [--ext-path ext/Lidarr] [--out-path ext/Lidarr/_output/net8.0] [--docker-tag <tag>]

Options:
  --mode        docker (default) to extract from ghcr.io/hotio/lidarr:<tag>, or source to clone/build Lidarr.
  --branch      Lidarr branch for MODE=source (default: plugins)
  --ext-path    Path to ext/Lidarr working directory (default: ext/Lidarr)
  --out-path    Destination for assemblies (default: ext/Lidarr/_output/net8.0)
  --docker-tag  Docker tag for plugins image (default: env LIDARR_DOCKER_VERSION or pr-plugins-3.1.2.4913)

Environment:
  LIDARR_DOCKER_VERSION  Overrides --docker-tag when set.

The script sets and prints LIDARR_PATH on success.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --mode) MODE="$2"; shift 2 ;;
    --branch) BRANCH="$2"; shift 2 ;;
    --ext-path) EXT_PATH="$2"; shift 2 ;;
    --out-path) OUT_PATH="$2"; shift 2 ;;
    --docker-tag) DOCKER_TAG="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

echo "==> Setting up Lidarr assemblies (mode: $MODE)"

mkdir -p "$OUT_PATH"

if [[ "$MODE" == "docker" ]]; then
  if ! command -v docker >/dev/null 2>&1; then
    echo "Error: docker not found. Install Docker or use --mode source" >&2
    exit 1
  fi
  IMAGE="ghcr.io/hotio/lidarr:${DOCKER_TAG}"
  echo "Using Docker image: $IMAGE"
  docker pull "$IMAGE" >/dev/null
  cid=$(docker create "$IMAGE")
  # Copy entire /app/bin to OUT_PATH
  docker cp "$cid:/app/bin/." "$OUT_PATH/"
  docker rm -f "$cid" >/dev/null
  echo "Assemblies copied to: $OUT_PATH"
elif [[ "$MODE" == "source" ]]; then
  echo "Cloning/building Lidarr ($BRANCH) into $EXT_PATH"
  mkdir -p "$(dirname "$EXT_PATH")"
  if [[ ! -d "$EXT_PATH/.git" ]]; then
    git clone --branch "$BRANCH" --depth 1 https://github.com/Lidarr/Lidarr.git "$EXT_PATH"
  else
    (cd "$EXT_PATH" && git fetch origin "$BRANCH" && git reset --hard "origin/$BRANCH")
  fi
  pushd "$EXT_PATH/src" >/dev/null
  dotnet restore Lidarr.sln
  dotnet build Lidarr.sln -c Release
  popd >/dev/null
  # Prefer standard _output
  if [[ ! -d "$OUT_PATH" ]]; then
    alt="$EXT_PATH/src/NzbDrone.Core/bin/Release/net8.0"
    if [[ -d "$alt" ]]; then
      OUT_PATH="$alt"
    fi
  fi
  echo "Built Lidarr. Assemblies at: $OUT_PATH"
else
  echo "Invalid --mode: $MODE" >&2
  exit 2
fi

# Verify expected Lidarr assemblies exist
shopt -s nullglob
matches=( "$OUT_PATH"/Lidarr.*.dll )
if (( ${#matches[@]} == 0 )); then
  echo "Warning: No Lidarr.*.dll found in $OUT_PATH" >&2
fi

# Export helper
export LIDARR_PATH="$(cd "$OUT_PATH" && pwd)"
echo "LIDARR_PATH=$LIDARR_PATH"
echo "To use in this shell: export LIDARR_PATH=\"$LIDARR_PATH\""

echo "==> Done. You can now build: dotnet build Brainarr.sln -c Release -p:LidarrPath=\"$LIDARR_PATH\""
