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
plugin_version=$(grep -Po '"version":\s*"\K[^\"]+' plugin.json | head -n1 || true)
min_version=$(grep -Po '"minimumVersion":\s*"\K[^\"]+' plugin.json | head -n1 || true)

# Normalize potential CR from Windows line endings
plugin_version=${plugin_version%$'\r'}
min_version=${min_version%$'\r'}

[[ -n "$plugin_version" ]] || fail "Could not read plugin version from plugin.json"
[[ -n "$min_version" ]] || fail "Could not read minimumVersion from plugin.json"

# README badge version
readme_version=$(grep -Po 'version-\K[0-9]+\.[0-9]+\.[0-9]+' README.md | head -n1 || true)
[[ -n "$readme_version" ]] || fail "Could not find version badge in README.md"
[[ "$readme_version" == "$plugin_version" ]] || fail "README badge version ($readme_version) != plugin.json version ($plugin_version)"

# docs/PLUGIN_MANIFEST.md example versions must match
mapfile -t doc_versions < <(grep -Po '"version":\s*"\K[0-9]+\.[0-9]+\.[0-9]+' docs/PLUGIN_MANIFEST.md | tr -d '\r' | sort -u)
for v in "${doc_versions[@]}"; do
  v=${v%$'\r'}
  [[ "$v" == "$plugin_version" ]] || fail "docs/PLUGIN_MANIFEST.md version example ($v) does not match plugin.json ($plugin_version)"
done

# minimumVersion mentions must match, exclude archive
mapfile -t min_mentions < <(grep -hRPo --exclude-dir=archive '"minimumVersion":\s*"\K[^\"]+' docs | tr -d '\r' | sort -u)
for mv in "${min_mentions[@]}"; do
  mv=${mv%$'\r'}
  [[ "$mv" == "$min_version" ]] || fail "docs minimumVersion mention ($mv) != plugin.json minimumVersion ($min_version)"
done

# No old minimum version leftovers (like 4.0.0.0) outside archive
if grep -R "4\.0\.0\.0" docs wiki-content | grep -v /archive/ >/dev/null 2>&1; then
  fail "Found legacy minimum version 4.0.0.0 in docs/wiki"
fi

# Path layout: no ownerless plugin paths
bad_paths=(
  "/plugins/Brainarr"
  "C:\\ProgramData\\Lidarr\\plugins\\Brainarr"
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
  "wiki-content/Home.md"
)
for f in "${must_have[@]}"; do
  if ! grep -Eq "Requires Lidarr \\*{0,2}${min_version}\\+\\*{0,2} on the \\*{0,2}plugins/nightly\\*{0,2} branch" "$f"; then
    fail "Missing compatibility notice in $f"
  fi
done

# Provider matrix alignment between README, wiki, and docs
extract_matrix() {
  local file="$1"
  awk '
    /<!-- PROVIDER_MATRIX_START -->/ { in_block=1; next }
    /<!-- PROVIDER_MATRIX_END -->/ { in_block=0; exit }
    { if (in_block) print }
  ' "$file" | tr -d '\r' | sed 's/[[:space:]]\+$//'
}

matrix_docs=$(extract_matrix "docs/PROVIDER_MATRIX.md")
[[ -n "$matrix_docs" ]] || fail "Missing provider matrix block in docs/PROVIDER_MATRIX.md"

matrix_readme=$(extract_matrix "README.md")
[[ -n "$matrix_readme" ]] || fail "Missing provider matrix block in README.md"

matrix_wiki=$(extract_matrix "wiki-content/Home.md")
[[ -n "$matrix_wiki" ]] || fail "Missing provider matrix block in wiki-content/Home.md"

if [[ "$matrix_docs" != "$matrix_readme" ]]; then
  diff <(printf '%s\n' "$matrix_docs") <(printf '%s\n' "$matrix_readme") || true
  fail "Provider matrix mismatch between docs/PROVIDER_MATRIX.md and README.md"
fi

if [[ "$matrix_docs" != "$matrix_wiki" ]]; then
  diff <(printf '%s\n' "$matrix_docs") <(printf '%s\n' "$matrix_wiki") || true
  fail "Provider matrix mismatch between docs/PROVIDER_MATRIX.md and wiki-content/Home.md"
fi

expected_release="Latest release: **v${plugin_version}**"
grep -F "$expected_release" README.md >/dev/null 2>&1 || fail "README missing latest release line ($expected_release)"
grep -F "$expected_release" wiki-content/Home.md >/dev/null 2>&1 || fail "Wiki Home missing latest release line ($expected_release)"

if ! grep -F "Brainarr Provider Matrix (v${plugin_version})" docs/PROVIDER_MATRIX.md >/dev/null 2>&1; then
  fail "docs/PROVIDER_MATRIX.md header not updated for v${plugin_version}"
fi

ok "Docs consistency checks passed (version=$plugin_version, minimumVersion=$min_version)"
