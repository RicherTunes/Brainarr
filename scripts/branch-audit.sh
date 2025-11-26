#!/usr/bin/env bash
# Non-destructive branch audit helper
# Summarizes differences vs <remote>/<main>, categorizes by file types/paths.

set -euo pipefail

REMOTE=${1:-origin}
BASE=${2:-main}

BASE_REF="$REMOTE/$BASE"

if ! git rev-parse --verify "$BASE_REF" >/dev/null 2>&1; then
  echo "Error: base ref $BASE_REF not found. Run: git fetch --all --prune" >&2
  exit 1
fi

echo "# Branch Audit Report (vs $BASE_REF)"
echo "generated: $(date -u +%FT%TZ)"
echo

list=$(git branch -r | sed 's/^\s*//' | grep -v -- '->' | grep "^$REMOTE/" | grep -v "^$REMOTE/pr/" | grep -v "^$REMOTE/$BASE$" | grep -v "^$REMOTE/dependabot/")

while read -r ref; do
  [[ -z "$ref" ]] && continue
  br=${ref#"$REMOTE/"}
  counts=$(git rev-list --left-right --count "$BASE_REF...$ref" 2>/dev/null || echo "0	0")
  ahead=$(awk '{print $1}' <<<"$counts")
  behind=$(awk '{print $2}' <<<"$counts")
  last=$(git log -1 --pretty=format:%h\ %ci\ %an:\ %s "$ref" 2>/dev/null || echo "-")
  files=$(git diff --name-only "$BASE_REF...$ref" | wc -l | tr -d ' ')
  # categories
  names=$(git diff --name-only "$BASE_REF...$ref")
  cat="code"
  if [[ -n "$names" ]]; then
    if echo "$names" | grep -Eq '^docs/|\.md$|^wiki-content/'; then cat="docs"; fi
    if echo "$names" | grep -Eq '^\.github/|^scripts/|\.yml$'; then cat="$cat,ci"; fi
    if echo "$names" | grep -Eq 'Services/Security|Security|Certificate|ApiKey'; then cat="$cat,security"; fi
    if echo "$names" | grep -Eq 'RateLimit|RateLimiting'; then cat="$cat,rate-limit"; fi
    if echo "$names" | grep -Eq 'Logging|Logger'; then cat="$cat,logging"; fi
    if echo "$names" | grep -Eq 'Performance|Benchmark'; then cat="$cat,perf"; fi
  fi
  # top dirs
  top=$(echo "$names" | awk -F/ '{print $1"/"$2}' | sort -u | tr '\n' ' ')
  printf "%s | ahead:%s behind:%s files:%s | cats:%s | last:%s | dirs:%s\n" "$ref" "$ahead" "$behind" "$files" "$cat" "$last" "$top"
done <<<"$list"

echo
echo "Tip: to test a branch locally without committing, try:\n  git checkout -B audit/<name> $BASE_REF && git merge --no-ff $REMOTE/<name> --no-edit || true\n  LIDARR_DOCKER_VERSION=pr-plugins-2.13.3.4692 ./setup-lidarr.sh --mode docker\n  dotnet build Brainarr.sln -c Release -p:LidarrPath=\"$PWD/ext/Lidarr/_output/net8.0\"\n  ~/.dotnet/dotnet test Brainarr.sln -c Release --no-build --filter \"TestCategory=Unit|Category=Unit\""
