#!/usr/bin/env bash
set -euo pipefail

show_help() {
  cat <<'EOF'
Usage: ./setup-lidarr.sh [--mode docker|source] [--branch plugins] [--ext-path ext/Lidarr] [--out-path ext/Lidarr/_output/net6.0] [--docker-tag <tag>]

Options:
  --mode        docker (default) to extract from ghcr.io/hotio/lidarr:<tag>, or source to clone/build Lidarr.
  --branch      Lidarr branch for MODE=source (default: plugins)
  --ext-path    Path to ext/Lidarr working directory (default: ext/Lidarr)
  --out-path    Destination for assemblies (default: ext/Lidarr/_output/net6.0)
  --docker-tag  Docker tag for plugins image (default: env LIDARR_DOCKER_VERSION or pr-plugins-2.13.3.4692)

Environment:
  LIDARR_DOCKER_VERSION  Overrides --docker-tag when set.

The script sets and prints LIDARR_PATH on success.
EOF
}

MODE=docker
BRANCH=plugins
EXT_PATH="ext/Lidarr"
OUT_PATH="ext/Lidarr/_output/net6.0"
DOCKER_TAG="${LIDARR_DOCKER_VERSION:-pr-plugins-2.13.3.4692}"

while [ $# -gt 0 ]; do
  case "$1" in
    --mode) MODE="$2"; shift 2;;
    --branch) BRANCH="$2"; shift 2;;
    --ext-path) EXT_PATH="$2"; shift 2;;
    --out-path) OUT_PATH="$2"; shift 2;;
    --docker-tag) DOCKER_TAG="$2"; shift 2;;
    -h|--help) show_help; exit 0;;
    *) echo "Unknown argument: $1" >&2; show_help; exit 1;;
  esac
done

mkdir -p "$OUT_PATH"

if [ "$MODE" = "docker" ]; then
  IMAGE="ghcr.io/hotio/lidarr:${DOCKER_TAG}"
  echo "Using Docker image: $IMAGE"
  docker pull "$IMAGE" >/dev/null
  cid=$(docker create "$IMAGE")
  docker cp "$cid:/app/bin/." "$OUT_PATH/"
  docker rm -f "$cid" >/dev/null
elif [ "$MODE" = "source" ]; then
  if [ ! -d "$EXT_PATH/.git" ]; then
    echo "Cloning Lidarr:$BRANCH into $EXT_PATH"
    git clone --branch "$BRANCH" --depth 1 https://github.com/Lidarr/Lidarr.git "$EXT_PATH" >/dev/null
  else
    echo "Updating Lidarr at $EXT_PATH"
    git -C "$EXT_PATH" fetch origin "$BRANCH"
    git -C "$EXT_PATH" reset --hard "origin/$BRANCH"
  fi
  # Try to use prebuilt output if available
  if [ ! -d "$OUT_PATH" ] || [ -z "$(ls -A "$OUT_PATH" 2>/dev/null || true)" ]; then
    echo "Warning: _output path not found. You may need to build Lidarr locally and copy assemblies." >&2
  fi
else
  echo "Invalid mode: $MODE" >&2; exit 1
fi

export LIDARR_PATH="$(cd "$OUT_PATH" && pwd)"
echo "LIDARR_PATH=$LIDARR_PATH"
ls -1 "$LIDARR_PATH" | head -20 || true

