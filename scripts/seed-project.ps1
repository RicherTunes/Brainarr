param(
  [string]$Owner,
  [ValidateSet('user','org')][string]$Scope = 'user',
  [int]$ProjectNumber,
  [string]$Repo = 'RicherTunes/Brainarr',
  [string]$TasksPath = 'tasks/seed.json',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Write-Info($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Ok($msg) { Write-Host $msg -ForegroundColor Green }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host $msg -ForegroundColor Red }

$token = $env:PROJECTS_TOKEN
if (-not $token) {
  Write-Err 'PROJECTS_TOKEN not set. Export a classic PAT with repo, project scopes.'
  exit 1
}

if (-not (Test-Path $TasksPath)) {
  Write-Err "Tasks file not found: $TasksPath"
  exit 1
}

$raw = Get-Content $TasksPath -Raw
try {
  $config = $raw | ConvertFrom-Json -Depth 10
} catch {
  Write-Err "Failed to parse JSON in $TasksPath: $($_.Exception.Message)"
  exit 1
}

if (-not $Owner) { $Owner = $config.owner }
if (-not $ProjectNumber) { $ProjectNumber = [int]$config.projectNumber }
if (-not $Repo) { $Repo = $config.repo }
if ($config.scope) { $Scope = $config.scope }

if (-not $Owner -or -not $ProjectNumber) {
  Write-Err 'Owner and ProjectNumber are required (either via params or seed.json).'
  exit 1
}

if (-not $config.tasks -or $config.tasks.Count -eq 0) {
  Write-Err 'No tasks provided in seed.json.'
  exit 1
}

$headersGraph = @{ Authorization = "bearer $token"; 'User-Agent' = 'Brainarr-Project-Seeder'; Accept = 'application/vnd.github+json' }
$headersRest = @{ Authorization = "token $token"; 'User-Agent' = 'Brainarr-Project-Seeder'; Accept = 'application/vnd.github+json' }

function Invoke-GraphQL([string]$Query, $Variables) {
  $body = @{ query = $Query; variables = $Variables } | ConvertTo-Json -Depth 50
  $resp = Invoke-RestMethod -Method Post -Uri 'https://api.github.com/graphql' -Headers $headersGraph -ContentType 'application/json' -Body $body
  if ($null -ne $resp.errors) {
    $err = ($resp.errors | ConvertTo-Json -Depth 50)
    throw "GraphQL error: $err"
  }
  return $resp.data
}

Write-Info "Resolving ProjectV2 id for $Owner #$ProjectNumber ($Scope)"

$projQuery = @"
query(
  $login:String!,
  $num:Int!
) {
  user(login:$login) @include(if: %(isUser)s) {
    projectV2(number:$num) {
      id
      title
      fields(first:50) {
        nodes {
          __typename
          id
          name
          dataType
          ... on ProjectV2SingleSelectField {
            options { id name }
          }
        }
      }
    }
  }
  organization(login:$login) @include(if: %(isOrg)s) {
    projectV2(number:$num) {
      id
      title
      fields(first:50) {
        nodes {
          __typename
          id
          name
          dataType
          ... on ProjectV2SingleSelectField {
            options { id name }
          }
        }
      }
    }
  }
}
"@

$projQuery = $projQuery.Replace('%(isUser)s', $(if ($Scope -eq 'user') { 'true' } else { 'false' }))
$projQuery = $projQuery.Replace('%(isOrg)s', $(if ($Scope -eq 'org') { 'true' } else { 'false' }))

$data = Invoke-GraphQL $projQuery @{ login = $Owner; num = $ProjectNumber }

$project = if ($Scope -eq 'user') { $data.user.projectV2 } else { $data.organization.projectV2 }
if (-not $project -or -not $project.id) {
  Write-Err "Project not found. Check Owner/Scope/ProjectNumber."
  exit 1
}

$projectId = $project.id
Write-Ok "Project resolved: $($project.title) ($projectId)"

$statusField = $null
foreach ($f in $project.fields.nodes) {
  if ($f.name -eq 'Status' -and $f.__typename -eq 'ProjectV2SingleSelectField') { $statusField = $f; break }
}

$defaultStatusName = if ($config.status) { [string]$config.status } else { $null }

function Get-StatusOptionId([string]$name) {
  if (-not $statusField) { return $null }
  foreach ($opt in $statusField.options) {
    if ($opt.name -ieq $name) { return $opt.id }
  }
  return $null
}

$ownerRepoParts = $Repo.Split('/')
if ($ownerRepoParts.Count -ne 2) { throw "Repo must be in 'owner/name' form. Got: $Repo" }
$repoOwner = $ownerRepoParts[0]
$repoName  = $ownerRepoParts[1]

Write-Info "Seeding $($config.tasks.Count) task(s) into repo $Repo and project $Owner/#$ProjectNumber"

$addMutation = @"
mutation(
  $projectId:ID!,
  $contentId:ID!
) {
  addProjectV2ItemById(input:{projectId:$projectId, contentId:$contentId}) {
    item { id }
  }
}
"@

$updateStatusMutation = @"
mutation(
  $projectId:ID!,
  $itemId:ID!,
  $fieldId:ID!,
  $optionId:String!
) {
  updateProjectV2ItemFieldValue(input:{
    projectId:$projectId,
    itemId:$itemId,
    fieldId:$fieldId,
    value:{ singleSelectOptionId:$optionId }
  }) {
    projectV2Item { id }
  }
}
"@

$results = @()
foreach ($t in $config.tasks) {
  $title = [string]$t.title
  if ([string]::IsNullOrWhiteSpace($title)) { Write-Warn 'Skipping task with empty title'; continue }
  $body  = [string]$t.body
  $labels = @()
  if ($t.labels) { $labels = @($t.labels | ForEach-Object { [string]$_ }) }
  $assignees = @()
  if ($t.assignees) { $assignees = @($t.assignees | ForEach-Object { [string]$_ }) }
  $statusName = if ($t.status) { [string]$t.status } else { $defaultStatusName }

  Write-Info "Creating issue: $title"
  if ($DryRun) {
    Write-Host "DRY-RUN would create issue in $Repo with labels=[$($labels -join ', ')]" -ForegroundColor DarkGray
    $results += @{ title=$title; issue_url=$null; item_id=$null }
    continue
  }

  $issueReq = @{ title = $title; body = $body }
  if ($labels.Count -gt 0) { $issueReq.labels = $labels }
  if ($assignees.Count -gt 0) { $issueReq.assignees = $assignees }
  $issueResp = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/issues" -Headers $headersRest -ContentType 'application/json' -Body ($issueReq | ConvertTo-Json -Depth 10)
  $issueNodeId = $issueResp.node_id
  $issueUrl    = $issueResp.html_url
  Write-Ok "Created issue: $issueUrl"

  Write-Info 'Adding issue to project…'
  $addData = Invoke-GraphQL $addMutation @{ projectId = $projectId; contentId = $issueNodeId }
  $itemId = $addData.addProjectV2ItemById.item.id
  Write-Ok "Added to project as item $itemId"

  if ($statusName -and $statusField) {
    $optId = Get-StatusOptionId $statusName
    if ($optId) {
      Write-Info "Setting Status='$statusName'"
      $null = Invoke-GraphQL $updateStatusMutation @{ projectId=$projectId; itemId=$itemId; fieldId=$statusField.id; optionId=$optId }
    } else {
      Write-Warn "Status option not found: $statusName. Skipping field update."
    }
  }

  $results += @{ title=$title; issue_url=$issueUrl; item_id=$itemId }
}

Write-Ok "Seeding complete. Created $($results.Count) task(s)."
if (-not $DryRun) {
  $results | ForEach-Object { if ($_.issue_url) { Write-Host " - $_.title -> $_.issue_url" -ForegroundColor Green } }
}

Write-Host "Usage tips:" -ForegroundColor Cyan
Write-Host " - Set repo variable PROJECT_URL to your Project v2 URL for auto-add workflow." -ForegroundColor Cyan
Write-Host " - PROJECTS_TOKEN must have 'repo' and 'project' scopes." -ForegroundColor Cyan
