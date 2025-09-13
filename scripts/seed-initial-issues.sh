#!/usr/bin/env bash

set -euo pipefail

OWNER="RicherTunes"
REPO="Brainarr"
PROJECT_NUMBER=1
TOKEN="${GITHUB_TOKEN:-}"

usage() {
  cat <<USAGE
Usage: $(basename "$0") [options]
  -o, --owner <owner>            GitHub user/org (default: $OWNER)
  -r, --repo <repo>              Repository name (default: $REPO)
  -p, --project <num>            User Project v2 number (default: $PROJECT_NUMBER)
  -t, --token <token>            GitHub token (default: env GITHUB_TOKEN)

Requires: curl, jq
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -o|--owner) OWNER="$2"; shift 2;;
    -r|--repo) REPO="$2"; shift 2;;
    -p|--project|--project-number) PROJECT_NUMBER="$2"; shift 2;;
    -t|--token) TOKEN="$2"; shift 2;;
    -h|--help) usage; exit 0;;
    *) echo "Unknown argument: $1" >&2; usage; exit 1;;
  esac
done

require_cmd() { command -v "$1" >/dev/null 2>&1 || { echo "Missing required command: $1" >&2; exit 1; }; }
require_cmd curl
require_cmd jq

if [[ -z "$TOKEN" ]]; then
  echo "Provide a GitHub token via --token or GITHUB_TOKEN env var" >&2
  exit 1
fi

REST_H=(
  -H "Authorization: token $TOKEN"
  -H "Accept: application/vnd.github+json"
  -H "User-Agent: seed-initial-issues"
)

gh_rest() {
  local method="$1" url="$2" data="${3:-}"
  if [[ -n "$data" ]]; then
    curl -sS "${REST_H[@]}" -X "$method" "$url" -d "$data"
  else
    curl -sS "${REST_H[@]}" -X "$method" "$url"
  fi
}

create_label() {
  local name="$1" color="$2" desc="$3"
  local payload
  payload=$(jq -n --arg name "$name" --arg color "$color" --arg desc "$desc" '{name:$name,color:$color,description:$desc}')
  local code
  code=$(curl -sS -o /dev/null -w "%{http_code}" "${REST_H[@]}" \
    -X POST "https://api.github.com/repos/$OWNER/$REPO/labels" -d "$payload")
  if [[ "$code" == "201" ]]; then
    echo "Created label: $name"
  elif [[ "$code" == "422" ]]; then
    echo "Label exists: $name"
  else
    echo "Label $name returned HTTP $code" >&2
  fi
}

gh_graphql() {
  local query="$1" variables_json="$2"
  curl -sS -H "Authorization: bearer $TOKEN" -H "User-Agent: seed-initial-issues" \
    -X POST https://api.github.com/graphql \
    -d "$(jq -n --arg q "$query" --argjson vars "$variables_json" '{query:$q, variables:$vars}')"
}

get_user_project_id() {
  local q variables resp id
  read -r -d '' q <<'Q'
query($login: String!, $number: Int!) {
  user(login: $login) {
    projectV2(number: $number) { id }
  }
}
Q
  variables=$(jq -n --arg login "$OWNER" --argjson number "$PROJECT_NUMBER" '{login:$login, number:$number}')
  resp=$(gh_graphql "$q" "$variables")
  id=$(jq -r '.data.user.projectV2.id // empty' <<<"$resp")
  [[ -n "$id" ]] && echo "$id" || return 1
}

add_issue_to_project() {
  local project_id="$1" content_id="$2" q variables
  read -r -d '' q <<'Q'
mutation($projectId: ID!, $contentId: ID!) {
  addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
    item { id }
  }
}
Q
  variables=$(jq -n --arg projectId "$project_id" --arg contentId "$content_id" '{projectId:$projectId, contentId:$contentId}')
  gh_graphql "$q" "$variables" >/dev/null
}

new_issue() {
  local title="$1" labels_json="$2" body="$3" payload resp
  payload=$(jq -n --arg title "$title" --arg body "$body" --argjson labels "$labels_json" '{title:$title, body:$body, labels:$labels}')
  resp=$(gh_rest POST "https://api.github.com/repos/$OWNER/$REPO/issues" "$payload")
  echo "$resp"
}

echo "==> Ensuring base labels"
create_label "ci" "1d76db" "Continuous Integration"
create_label "task" "d4c5f9" "General engineering task/chore"
create_label "documentation" "0075ca" "Documentation changes"
create_label "needs-triage" "fbca04" "Needs initial triage"

echo "==> Resolving Project v2 ID for $OWNER #$PROJECT_NUMBER"
PROJECT_ID=$(get_user_project_id) || { echo "Failed to resolve Project v2 ID" >&2; exit 1; }

make_issue() {
  local TITLE="$1" LABELS_JSON="$2" BODY="$3" out num node url
  out=$(new_issue "$TITLE" "$LABELS_JSON" "$BODY")
  num=$(jq -r '.number' <<<"$out")
  node=$(jq -r '.node_id' <<<"$out")
  url=$(jq -r '.html_url' <<<"$out")
  echo "Created issue #$num: $url"
  if [[ -n "$node" && "$node" != "null" ]]; then
    add_issue_to_project "$PROJECT_ID" "$node"
    echo "  ↳ Added to Project"
  fi
}

BODY1=$(cat <<'EOF'
AGENTS decision: enforce `shell: bash` for POSIX-style steps across the matrix.

Tasks
- [ ] Set `defaults.run.shell: bash` at workflow level where applicable
- [ ] Explicitly set `shell: bash` for POSIX steps on Windows runners
- [ ] Verify Windows-only steps use `shell: pwsh` when intended
- [ ] Align `build.sh` and `test-local-ci.sh` with workflow shells

Acceptance
- [ ] All workflows use consistent shells; no mixed default shells
- [ ] CI passes on Linux/macOS/Windows
EOF
)

BODY2=$(cat <<'EOF'
AGENTS decision: extract required Lidarr assemblies from the Hotio `plugins` branch Docker image and publish as an artifact for matrix jobs.

Tasks
- [ ] Create a dedicated job that pulls `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` and exports `/app/bin` assemblies to `ext/Lidarr-docker/_output/net6.0/`
- [ ] Upload artifact `lidarr-assemblies-net6.0`
- [ ] Replace re-extraction in dependent jobs with artifact download

Acceptance
- [ ] Only one extraction job runs per workflow
- [ ] All matrix jobs consume the artifact
EOF
)

BODY3=$(cat <<'EOF'
Add an early sanity check in build/test jobs to fail if `ext/Lidarr-docker/_output/net6.0/` is absent or empty.

Tasks
- [ ] Add pre-build step to assert required assemblies exist
- [ ] Provide actionable error message pointing to extraction job logs

Acceptance
- [ ] Missing assemblies cause early, clear failure
EOF
)

BODY4=$(cat <<'EOF'
Refactor CodeQL and security workflows to reuse the same extraction logic (composite action or reusable workflow) used by CI.

Tasks
- [ ] Create composite action or reusable workflow for Docker-based extraction
- [ ] Consume it in CI, CodeQL, nightly jobs

Acceptance
- [ ] No duplicated shell blocks for extraction
EOF
)

BODY5=$(cat <<'EOF'
Automate monitoring of the Hotio `plugins` tag/digest and open an issue/PR when drift is detected.

Tasks
- [ ] Add scheduled job to compare current `LIDARR_DOCKER_VERSION`/digest vs latest
- [ ] Open issue with changelog context when drift occurs

Acceptance
- [ ] Drift results in a single actionable issue
EOF
)

BODY6=$(cat <<'EOF'
Update BUILD.md/DEVELOPMENT.md to explain Docker-based extraction and artifact usage, plus quick local dev setup.

Tasks
- [ ] BUILD.md – outline extraction, artifact, env vars
- [ ] DEVELOPMENT.md – local workflow, test scripts, troubleshooting

Acceptance
- [ ] Docs match AGENTS decisions and current CI
EOF
)

echo "==> Creating issues and adding to Project"
make_issue "CI: Enforce bash shell across POSIX workflows" '["ci","task","needs-triage"]' "$BODY1"
make_issue "CI: Centralize Lidarr assemblies extraction + artifact" '["ci","task","needs-triage"]' "$BODY2"
make_issue "CI: Fail-fast if Lidarr assemblies missing" '["ci","task","needs-triage"]' "$BODY3"
make_issue "CI: Deduplicate extraction in CodeQL/security scans" '["ci","task","needs-triage"]' "$BODY4"
make_issue "CI: Keep LIDARR_DOCKER_VERSION current for plugins branch" '["ci","task","needs-triage"]' "$BODY5"
make_issue "Docs: Document Docker-based extraction + local dev flow" '["documentation","needs-triage"]' "$BODY6"

echo "==> Done"

