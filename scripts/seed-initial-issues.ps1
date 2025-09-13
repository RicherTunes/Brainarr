#!/usr/bin/env pwsh
param(
  [string]$Owner = "RicherTunes",
  [string]$Repo = "Brainarr",
  [int]$ProjectNumber = 1,
  [string]$Token = $env:GITHUB_TOKEN
)

if (-not $Token) {
  Write-Error "Provide a GitHub token via -Token or set GITHUB_TOKEN env var."; exit 1
}

$RestHeaders = @{
  Authorization = "token $Token";
  Accept        = "application/vnd.github+json";
  'User-Agent'  = "seed-initial-issues"
}

function Invoke-GHRest([string]$Method, [string]$Uri, $Body) {
  $json = if ($Body) { ($Body | ConvertTo-Json -Depth 8) } else { $null }
  $params = @{ Method=$Method; Uri=$Uri; Headers=$RestHeaders }
  if ($json) { $params["Body"] = $json }
  Invoke-RestMethod @params
}

function Invoke-GHGraphQL($Query, $Variables) {
  $Uri = 'https://api.github.com/graphql'
  $Headers = @{ Authorization = "bearer $Token"; 'User-Agent'="seed-initial-issues" }
  $Body = @{ query = $Query; variables = $Variables }
  Invoke-RestMethod -Method POST -Uri $Uri -Headers $Headers -Body ($Body | ConvertTo-Json -Depth 8)
}

function Ensure-Label([string]$Name, [string]$Color, [string]$Description) {
  try {
    Invoke-GHRest -Method POST -Uri "https://api.github.com/repos/$Owner/$Repo/labels" -Body @{ name=$Name; color=$Color; description=$Description } | Out-Null
    Write-Host "Created label: $Name"
  } catch {
    if ($_.Exception.Response.StatusCode.Value__ -eq 422) { Write-Host "Label exists: $Name" }
    else { throw }
  }
}

function New-RepoIssue([string]$Title, [string[]]$Labels, [string]$Body) {
  $res = Invoke-GHRest -Method POST -Uri "https://api.github.com/repos/$Owner/$Repo/issues" -Body @{ title=$Title; labels=$Labels; body=$Body }
  return $res
}

function Get-UserProjectId([string]$Login, [int]$Number) {
$q = @'
query(
  $login: String!,
  $number: Int!
) {
  user(login: $login) {
    projectV2(number: $number) { id }
  }
}
'@
  $data = Invoke-GHGraphQL -Query $q -Variables @{ login=$Login; number=$Number }
  return $data.data.user.projectV2.id
}

function Add-IssueToProject([string]$ProjectId, [string]$IssueNodeId) {
$m = @'
mutation(
  $projectId: ID!,
  $contentId: ID!
) {
  addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
    item { id }
  }
}
'@
  Invoke-GHGraphQL -Query $m -Variables @{ projectId=$ProjectId; contentId=$IssueNodeId } | Out-Null
}

Write-Host "==> Ensuring base labels"
Ensure-Label -Name "ci" -Color "1d76db" -Description "Continuous Integration"
Ensure-Label -Name "task" -Color "d4c5f9" -Description "General engineering task/chore"
Ensure-Label -Name "documentation" -Color "0075ca" -Description "Documentation changes"
Ensure-Label -Name "needs-triage" -Color "fbca04" -Description "Needs initial triage"

$ProjectId = Get-UserProjectId -Login $Owner -Number $ProjectNumber
if (-not $ProjectId) { Write-Error "Could not resolve Project v2 ID for $Owner #$ProjectNumber"; exit 1 }

Write-Host "==> Seeding initial issues and adding to Project $ProjectNumber"

$items = @(
  @{ title = "CI: Enforce bash shell across POSIX workflows"; labels = @("ci","task","needs-triage"); body = @'
AGENTS decision: enforce `shell: bash` for POSIX-style steps across the matrix.

Tasks
- [ ] Set `defaults.run.shell: bash` at workflow level where applicable
- [ ] Explicitly set `shell: bash` for POSIX steps on Windows runners
- [ ] Verify Windows-only steps use `shell: pwsh` when intended
- [ ] Align `build.sh` and `test-local-ci.sh` with workflow shells

Acceptance
- [ ] All workflows use consistent shells; no mixed default shells
- [ ] CI passes on Linux/macOS/Windows
'@ },

  @{ title = "CI: Centralize Lidarr assemblies extraction + artifact"; labels = @("ci","task","needs-triage"); body = @'
AGENTS decision: extract required Lidarr assemblies from the Hotio `plugins` branch Docker image and publish as an artifact for matrix jobs.

Tasks
- [ ] Create a dedicated job that pulls `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` and exports `/app/bin` assemblies to `ext/Lidarr-docker/_output/net6.0/`
- [ ] Upload artifact `lidarr-assemblies-net6.0`
- [ ] Replace re-extraction in dependent jobs with artifact download

Acceptance
- [ ] Only one extraction job runs per workflow
- [ ] All matrix jobs consume the artifact
'@ },

  @{ title = "CI: Fail-fast if Lidarr assemblies missing"; labels = @("ci","task","needs-triage"); body = @'
Add an early sanity check in build/test jobs to fail if `ext/Lidarr-docker/_output/net6.0/` is absent or empty.

Tasks
- [ ] Add pre-build step to assert required assemblies exist
- [ ] Provide actionable error message pointing to extraction job logs

Acceptance
- [ ] Missing assemblies cause early, clear failure
'@ },

  @{ title = "CI: Deduplicate extraction in CodeQL/security scans"; labels = @("ci","task","needs-triage"); body = @'
Refactor CodeQL and security workflows to reuse the same extraction logic (composite action or reusable workflow) used by CI.

Tasks
- [ ] Create composite action or reusable workflow for Docker-based extraction
- [ ] Consume it in CI, CodeQL, nightly jobs

Acceptance
- [ ] No duplicated shell blocks for extraction
'@ },

  @{ title = "CI: Keep LIDARR_DOCKER_VERSION current for plugins branch"; labels = @("ci","task","needs-triage"); body = @'
Automate monitoring of the Hotio `plugins` tag/digest and open an issue/PR when drift is detected.

Tasks
- [ ] Add scheduled job to compare current `LIDARR_DOCKER_VERSION`/digest vs latest
- [ ] Open issue with changelog context when drift occurs

Acceptance
- [ ] Drift results in a single actionable issue
'@ },

  @{ title = "Docs: Document Docker-based extraction + local dev flow"; labels = @("documentation","needs-triage"); body = @'
Update BUILD.md/DEVELOPMENT.md to explain Docker-based extraction and artifact usage, plus quick local dev setup.

Tasks
- [ ] BUILD.md – outline extraction, artifact, env vars
- [ ] DEVELOPMENT.md – local workflow, test scripts, troubleshooting

Acceptance
- [ ] Docs match AGENTS decisions and current CI
'@ }
)

foreach ($it in $items) {
  $res = New-RepoIssue -Title $it.title -Labels $it.labels -Body $it.body
  $num = $res.number; $node = $res.node_id; $url = $res.html_url
  Write-Host ("Created issue #{0}: {1}" -f $num, $url)
  if ($node) { Add-IssueToProject -ProjectId $ProjectId -IssueNodeId $node; Write-Host "  ↳ Added to Project" }
}

Write-Host "==> Done"
