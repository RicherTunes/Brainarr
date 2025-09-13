#!/usr/bin/env python3
import json
import os
import sys
import urllib.request
import urllib.error

OWNER = os.environ.get("OWNER", "RicherTunes")
REPO = os.environ.get("REPO", "Brainarr")
PROJECT_NUMBER = int(os.environ.get("PROJECT_NUMBER", "1"))
TOKEN = os.environ.get("GITHUB_TOKEN")

API = "https://api.github.com"

def eprint(*args, **kwargs):
    print(*args, file=sys.stderr, **kwargs)

def http_request(method, url, body=None, headers=None):
    data = None
    if body is not None:
        if not isinstance(body, (bytes, bytearray)):
            body = json.dumps(body).encode("utf-8")
        data = body
    req = urllib.request.Request(url=url, data=data, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req) as resp:
            content = resp.read().decode("utf-8")
            if content:
                return resp.getcode(), json.loads(content)
            return resp.getcode(), None
    except urllib.error.HTTPError as e:
        content = e.read().decode("utf-8") if e.fp else ""
        try:
            payload = json.loads(content) if content else None
        except Exception:
            payload = {"raw": content}
        return e.code, payload
    except urllib.error.URLError as e:
        raise RuntimeError(f"Network error contacting {url}: {e}")

def rest_headers():
    return {
        "Authorization": f"token {TOKEN}",
        "Accept": "application/vnd.github+json",
        "User-Agent": "seed-initial-issues-python",
        "Content-Type": "application/json",
    }

def gh_graphql(query, variables):
    headers = {
        "Authorization": f"bearer {TOKEN}",
        "Accept": "application/vnd.github+json",
        "User-Agent": "seed-initial-issues-python",
        "Content-Type": "application/json",
    }
    code, body = http_request("POST", f"{API}/graphql", {"query": query, "variables": variables}, headers)
    if code >= 400:
        raise RuntimeError(f"GraphQL error {code}: {body}")
    return body

def ensure_label(name, color, description):
    code, _ = http_request(
        "POST",
        f"{API}/repos/{OWNER}/{REPO}/labels",
        {"name": name, "color": color, "description": description},
        rest_headers(),
    )
    if code == 201:
        print(f"Created label: {name}")
    elif code == 422:
        print(f"Label exists: {name}")
    else:
        eprint(f"Label {name} returned HTTP {code}")

def new_issue(title, labels, body):
    code, resp = http_request(
        "POST",
        f"{API}/repos/{OWNER}/{REPO}/issues",
        {"title": title, "labels": labels, "body": body},
        rest_headers(),
    )
    if code not in (200, 201):
        raise RuntimeError(f"Failed to create issue ({code}): {resp}")
    return resp

def get_user_project_id(login, number):
    q = (
        "query($login: String!, $number: Int!) {\n"
        "  user(login: $login) { projectV2(number: $number) { id } }\n"
        "}"
    )
    data = gh_graphql(q, {"login": login, "number": number})
    try:
        return data["data"]["user"]["projectV2"]["id"]
    except Exception:
        raise RuntimeError(f"Could not resolve Project v2 ID: {data}")

def add_issue_to_project(project_id, issue_node_id):
    m = (
        "mutation($projectId: ID!, $contentId: ID!) {\n"
        "  addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) { item { id } }\n"
        "}"
    )
    gh_graphql(m, {"projectId": project_id, "contentId": issue_node_id})

def main():
    global OWNER, REPO, PROJECT_NUMBER, TOKEN
    # Basic CLI parsing
    args = sys.argv[1:]
    i = 0
    while i < len(args):
        a = args[i]
        if a in ("-o", "--owner"):
            OWNER = args[i+1]; i += 2
        elif a in ("-r", "--repo"):
            REPO = args[i+1]; i += 2
        elif a in ("-p", "--project", "--project-number"):
            PROJECT_NUMBER = int(args[i+1]); i += 2
        elif a in ("-t", "--token"):
            TOKEN = args[i+1]; i += 2
        elif a in ("-h", "--help"):
            print("Usage: seed_initial_issues.py [-o owner] [-r repo] [-p project] [-t token]"); return 0
        else:
            eprint(f"Unknown argument: {a}"); return 2

    if not TOKEN:
        eprint("Provide a GitHub token via --token or GITHUB_TOKEN env var");
        return 1

    # quick network sanity
    try:
        code, _ = http_request("GET", f"{API}/rate_limit", headers=rest_headers())
        if code != 200:
            eprint(f"GitHub API unreachable, code {code}")
    except Exception as e:
        eprint(str(e)); return 1

    print("==> Ensuring base labels")
    ensure_label("ci", "1d76db", "Continuous Integration")
    ensure_label("task", "d4c5f9", "General engineering task/chore")
    ensure_label("documentation", "0075ca", "Documentation changes")
    ensure_label("needs-triage", "fbca04", "Needs initial triage")

    print(f"==> Resolving Project v2 ID for {OWNER} #{PROJECT_NUMBER}")
    project_id = get_user_project_id(OWNER, PROJECT_NUMBER)

    issues = [
        (
            "CI: Enforce bash shell across POSIX workflows",
            ["ci", "task", "needs-triage"],
            """AGENTS decision: enforce `shell: bash` for POSIX-style steps across the matrix.

Tasks
- [ ] Set `defaults.run.shell: bash` at workflow level where applicable
- [ ] Explicitly set `shell: bash` for POSIX steps on Windows runners
- [ ] Verify Windows-only steps use `shell: pwsh` when intended
- [ ] Align `build.sh` and `test-local-ci.sh` with workflow shells

Acceptance
- [ ] All workflows use consistent shells; no mixed default shells
- [ ] CI passes on Linux/macOS/Windows
""",
        ),
        (
            "CI: Centralize Lidarr assemblies extraction + artifact",
            ["ci", "task", "needs-triage"],
            """AGENTS decision: extract required Lidarr assemblies from the Hotio `plugins` branch Docker image and publish as an artifact for matrix jobs.

Tasks
- [ ] Create a dedicated job that pulls `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` and exports `/app/bin` assemblies to `ext/Lidarr-docker/_output/net6.0/`
- [ ] Upload artifact `lidarr-assemblies-net6.0`
- [ ] Replace re-extraction in dependent jobs with artifact download

Acceptance
- [ ] Only one extraction job runs per workflow
- [ ] All matrix jobs consume the artifact
""",
        ),
        (
            "CI: Fail-fast if Lidarr assemblies missing",
            ["ci", "task", "needs-triage"],
            """Add an early sanity check in build/test jobs to fail if `ext/Lidarr-docker/_output/net6.0/` is absent or empty.

Tasks
- [ ] Add pre-build step to assert required assemblies exist
- [ ] Provide actionable error message pointing to extraction job logs

Acceptance
- [ ] Missing assemblies cause early, clear failure
""",
        ),
        (
            "CI: Deduplicate extraction in CodeQL/security scans",
            ["ci", "task", "needs-triage"],
            """Refactor CodeQL and security workflows to reuse the same extraction logic (composite action or reusable workflow) used by CI.

Tasks
- [ ] Create composite action or reusable workflow for Docker-based extraction
- [ ] Consume it in CI, CodeQL, nightly jobs

Acceptance
- [ ] No duplicated shell blocks for extraction
""",
        ),
        (
            "CI: Keep LIDARR_DOCKER_VERSION current for plugins branch",
            ["ci", "task", "needs-triage"],
            """Automate monitoring of the Hotio `plugins` tag/digest and open an issue/PR when drift is detected.

Tasks
- [ ] Add scheduled job to compare current `LIDARR_DOCKER_VERSION`/digest vs latest
- [ ] Open issue with changelog context when drift occurs

Acceptance
- [ ] Drift results in a single actionable issue
""",
        ),
        (
            "Docs: Document Docker-based extraction + local dev flow",
            ["documentation", "needs-triage"],
            """Update BUILD.md/DEVELOPMENT.md to explain Docker-based extraction and artifact usage, plus quick local dev setup.

Tasks
- [ ] BUILD.md – outline extraction, artifact, env vars
- [ ] DEVELOPMENT.md – local workflow, test scripts, troubleshooting

Acceptance
- [ ] Docs match AGENTS decisions and current CI
""",
        ),
    ]

    print("==> Creating issues and adding to Project")
    for title, labels, body in issues:
        resp = new_issue(title, labels, body)
        num = resp.get("number")
        node = resp.get("node_id")
        url = resp.get("html_url")
        print(f"Created issue #{num}: {url}")
        if node:
            add_issue_to_project(project_id, node)
            print("  ↳ Added to Project")

    print("==> Done")
    return 0

if __name__ == "__main__":
    sys.exit(main())

