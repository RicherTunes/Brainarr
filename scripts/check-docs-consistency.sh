#!/usr/bin/env bash
set -euo pipefail

RED='\033[0;31m'
GRN='\033[0;32m'
NC='\033[0m'

fail() { echo -e "${RED}✖ $*${NC}"; exit 1; }
ok() { echo -e "${GRN}✔ $*${NC}"; }

root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")"/.. && pwd)"
cd "$root_dir"

# Extract versions
plugin_version=$(grep -Po '"version":\s*"\K[^"]+' plugin.json | head -n1 || true)
min_version=$(grep -Po '"minimumVersion":\s*"\K[^"]+' plugin.json | head -n1 || true)

[[ -n "$plugin_version" ]] || fail "Could not read plugin version from plugin.json"
[[ -n "$min_version" ]] || fail "Could not read minimumVersion from plugin.json"

# README badge version
readme_version=$(grep -Po 'version-\K[0-9]+\.[0-9]+\.[0-9]+' README.md | head -n1 || true)
[[ -n "$readme_version" ]] || fail "Could not find version badge in README.md"
[[ "$readme_version" == "$plugin_version" ]] || fail "README badge version ($readme_version) != plugin.json version ($plugin_version)"

# docs/PLUGIN_MANIFEST.md example versions must match
mapfile -t doc_versions < <(grep -Po '"version":\s*"\K[0-9]+\.[0-9]+\.[0-9]+' docs/PLUGIN_MANIFEST.md | sort -u)
for v in "${doc_versions[@]}"; do
  [[ "$v" == "$plugin_version" ]] || fail "docs/PLUGIN_MANIFEST.md version example ($v) does not match plugin.json ($plugin_version)"
done

# minimumVersion mentions must match, exclude archive
mapfile -t min_mentions < <(grep -RPo --exclude-dir=archive '"minimumVersion":\s*"\K[^"]+' docs | sort -u)
for mv in "${min_mentions[@]}"; do
  [[ "$mv" == "$min_version" ]] || fail "docs minimumVersion mention ($mv) != plugin.json minimumVersion ($min_version)"
done

# No old minimum version leftovers (like 4.0.0.0) outside archive
if grep -R "4\.0\.0\.0" docs wiki-content | grep -v /archive/ >/dev/null 2>&1; then
  fail "Found legacy minimum version 4.0.0.0 in docs/wiki"
fi

# Path layout: no ownerless plugin paths
bad_paths=(
  "/plugins/Brainarr"
  "C:\\\\\ProgramData\\\\Lidarr\\\\plugins\\\\Brainarr"
  "/config/plugins/Brainarr"
)
for pat in "${bad_paths[@]}"; do
  if grep -R --binary-files=without-match -F "$pat" README.md docs wiki-content >/dev/null 2>&1; then
    fail "Found deprecated path pattern: $pat"
  fi
done

# Compatibility block present in key pages
comp_text="Requires Lidarr $min_version+ on the plugins/nightly branch"
must_have=(
  "README.md"
  "docs/PROVIDER_GUIDE.md"
  "docs/DEPLOYMENT.md"
  "docs/USER_SETUP_GUIDE.md"
  "wiki-content/Installation.md"
)
for f in "${must_have[@]}"; do
  grep -F "Requires Lidarr" "$f" >/dev/null 2>&1 || fail "Missing compatibility notice in $f"
done

ok "Docs consistency checks passed (version=$plugin_version, minimumVersion=$min_version)"
