#!/usr/bin/env bash
set -euo pipefail

REMOTE=${1:-origin}
BASE_BRANCH=${2:-main}
BASE_REF="$REMOTE/$BASE_BRANCH"

echo "# Branch Audit Report (vs $BASE_REF)"; echo "generated: $(date -u +%FT%TZ)"; echo

# List non-main, non-dependabot, non-PR remote branches
branches=$(git branch -r | sed 's/^\s*//' | grep -v -- '->' | grep "^$REMOTE/" | grep -v "^$BASE_REF$" | grep -v "^$REMOTE/pr/" | grep -v "^$REMOTE/dependabot/")

while read -r ref; do
  [ -z "$ref" ] && continue
  br=${ref#"$REMOTE/"}
  counts=$(git rev-list --left-right --count "$BASE_REF...$ref" 2>/dev/null || echo "0\t0")
  ahead=$(echo "$counts" | awk '{print $1}')
  behind=$(echo "$counts" | awk '{print $2}')
  last=$(git log -1 --pretty=format:%h\ %ci\ %an:\ %s "$ref" 2>/dev/null || echo "-")
  files=$(git ls-tree -r --name-only "$ref" | wc -l | tr -d ' ')
  # quick category hints
  cats="code"
  if git diff --name-only "$BASE_REF...$ref" | grep -q "^docs/\|\.md$"; then cats="docs,$cats"; fi
  if git diff --name-only "$BASE_REF...$ref" | grep -qi "security\|secure\|sanit"; then cats="security,$cats"; fi
  if git diff --name-only "$BASE_REF...$ref" | grep -qi "rate.?limit"; then cats="rate-limit,$cats"; fi
  if git diff --name-only "$BASE_REF...$ref" | grep -qi "logging\|logger"; then cats="logging,$cats"; fi
  dirs=$(git diff --name-only "$BASE_REF...$ref" | awk -F/ '{print $1"/"$2}' | sort -u | tr '\n' ' ')
  echo "$ref | ahead:$ahead behind:$behind files:$files | cats:$cats | last:$last | dirs:${dirs:-/}"
done <<< "$branches"

