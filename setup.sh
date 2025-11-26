#!/usr/bin/env bash
set -euo pipefail

BRANCH=${BRANCH:-plugins}
ext_path_input=${EXT_PATH:-./ext/Lidarr}
CONFIGURATION=${CONFIGURATION:-Release}
RUN_TESTS=${RUN_TESTS:-false}
SKIP_LIDARR_SETUP=${SKIP_LIDARR_SETUP:-false}
SKIP_BUILD=${SKIP_BUILD:-false}
SKIP_RESTORE=${SKIP_RESTORE:-false}

log() {
  printf '==> %s\n' "$1"
}

fail() {
  printf 'ERROR: %s\n' "$1" >&2
  exit 1
}

command_exists() {
  command -v "$1" >/dev/null 2>&1
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"
if [[ ! -f Brainarr.sln ]]; then
  fail "Run setup.sh from the repository root (Brainarr.sln missing)."
fi


if [[ "$SKIP_LIDARR_SETUP" != "true" ]]; then
  command_exists git || fail "git is required."
fi
command_exists dotnet || fail ".NET SDK is required."

if [[ "$ext_path_input" = /* ]]; then
  ext_full_path="$ext_path_input"
else
  ext_full_path="$repo_root/${ext_path_input#./}"
fi

mkdir -p "$(dirname "$ext_full_path")"

resolve_lidarr_path() {
  local candidates=()
  if [[ -n "${LIDARR_PATH:-}" ]]; then
    candidates+=("$LIDARR_PATH")
  fi
  if [[ -f "$repo_root/ext/lidarr-path.txt" ]]; then
    local recorded
    recorded="$(< "$repo_root/ext/lidarr-path.txt")"
    candidates+=("$recorded")
  fi
  candidates+=(
    "$ext_full_path/_output/net8.0"
    "$ext_full_path/src/Lidarr/bin/Release/net8.0"
  )

  for candidate in "${candidates[@]}"; do
    [[ -n "$candidate" ]] || continue
    if [[ "$candidate" != /* ]]; then
      candidate="$repo_root/${candidate#./}"
    fi
    if [[ -f "$candidate/Lidarr.Core.dll" ]]; then
      (cd "$candidate" && pwd)
      return 0
    fi
  done
  return 1
}

setup_lidarr() {
  mkdir -p "$ext_full_path"
  if [[ ! -d "$ext_full_path/.git" ]]; then
    log "Cloning Lidarr repository (branch: $BRANCH)..."
    git clone --branch "$BRANCH" --depth 1 https://github.com/Lidarr/Lidarr.git "$ext_full_path" || fail "Failed to clone Lidarr."
  else
    log "Updating existing Lidarr checkout..."
    (cd "$ext_full_path" && git fetch origin "$BRANCH" && git reset --hard "origin/$BRANCH") || fail "Failed to update Lidarr checkout."
  fi

  log "Building Lidarr..."
  (cd "$ext_full_path/src" && dotnet restore Lidarr.sln && dotnet build Lidarr.sln -c Release) || fail "Lidarr build failed."
}

if [[ "$SKIP_LIDARR_SETUP" != "true" ]]; then
  setup_lidarr
fi

if ! lidarr_path="$(resolve_lidarr_path)"; then
  fail "Could not locate Lidarr assemblies. Run setup-lidarr.ps1 or set LIDARR_PATH."
fi

export LIDARR_PATH="$lidarr_path"
mkdir -p "$repo_root/ext"
printf '%s\n' "$lidarr_path" > "$repo_root/ext/lidarr-path.txt"
log "Using Lidarr assemblies from: $lidarr_path"

if [[ "$SKIP_RESTORE" != "true" ]]; then
  log "Restoring solution packages..."
  dotnet restore ./Brainarr.sln || fail "dotnet restore failed."
fi

if [[ "$SKIP_BUILD" != "true" ]]; then
  log "Building Brainarr plugin ($CONFIGURATION)..."
  dotnet build ./Brainarr.sln -c "$CONFIGURATION" -p:LidarrPath="$lidarr_path" || fail "dotnet build failed."
fi

if [[ "$RUN_TESTS" = "true" ]]; then
  if [[ -f ./Brainarr.Tests/Brainarr.Tests.csproj ]]; then
    log "Running tests..."
    dotnet test ./Brainarr.Tests/Brainarr.Tests.csproj -c "$CONFIGURATION" --no-build || fail "dotnet test failed."
  else
    log "Tests project not found; skipping tests."
  fi
fi

log "Setup complete. You are ready to work on Brainarr."
