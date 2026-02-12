Param(
  [int]$Port = 8686,
  [string]$LidarrTag = 'pr-plugins-3.1.2.4913',
  [switch]$SkipBuild,
  [int]$WaitSecs = 600
)

$ErrorActionPreference = 'Stop'

function Need($name) {
  if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
    throw "Missing dependency: $name"
  }
}

Need docker
Need dotnet
Need node

$root = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
Set-Location $root

if (-not (Test-Path 'ext/Lidarr/_output/net8.0/Lidarr.Core.dll')) {
  Write-Host 'Lidarr assemblies missing; running setup.ps1' -ForegroundColor Yellow
  ./setup.ps1
}

if (-not $SkipBuild) {
  dotnet restore ./Brainarr.Plugin/Brainarr.Plugin.csproj
  dotnet build ./Brainarr.Plugin/Brainarr.Plugin.csproj -c Release -p:LidarrPath="$(Resolve-Path 'ext/Lidarr/_output/net8.0')" -m:1
}

New-Item -ItemType Directory -Force -Path plugin-dist | Out-Null
$dll = Join-Path $root 'Brainarr.Plugin/bin/Release/net8.0/Lidarr.Plugin.Brainarr.dll'
if (-not (Test-Path $dll)) {
  $dll = Join-Path $root 'Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll'
}
if (-not (Test-Path $dll)) {
  Write-Host 'Could not find built plugin DLL. Build may have failed.' -ForegroundColor Red
  Get-ChildItem -Recurse -Filter Lidarr.Plugin.Brainarr.dll -Path (Join-Path $root 'Brainarr.Plugin') | Select-Object -First 10 | ForEach-Object { $_.FullName }
  exit 1
}
Copy-Item $dll plugin-dist/
Copy-Item plugin.json, manifest.json plugin-dist/
Write-Host "Staged plugin from: $dll"

$container = 'lidarr-ss'
docker rm -f $container 2>$null | Out-Null
docker pull "ghcr.io/hotio/lidarr:$LidarrTag"
docker run -d --name $container `
  -p "$Port:8686" `
  -v "$(Join-Path $root 'plugin-dist'):/config/plugins/RicherTunes/Brainarr:ro" `
  -e PUID=1000 -e PGID=1000 `
  "ghcr.io/hotio/lidarr:$LidarrTag" | Out-Null

Write-Host "Waiting for Lidarr UI on http://localhost:$Port ..."
$deadline = (Get-Date).AddSeconds($WaitSecs)
while ($true) {
  try { Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 -Uri "http://localhost:$Port/" | Out-Null; break } catch {}
  if ((Get-Date) -gt $deadline) {
    Write-Host 'Timeout waiting for Lidarr UI. Last container logs:' -ForegroundColor Red
    docker logs $container --tail 200 | Out-Host
    exit 1
  }
  Start-Sleep -Seconds 5
}

try {
  node -e "require('playwright')" 2>$null | Out-Null
} catch {
  npm i -D playwright@1.48.2
}
npx playwright install chromium | Out-Null

$env:LIDARR_BASE_URL = "http://localhost:$Port"
node scripts/snapshots/snap.mjs

Write-Host 'Screenshots saved under docs/assets/screenshots/' -ForegroundColor Green
Write-Host 'Stopping container...' -ForegroundColor Yellow
docker rm -f $container 2>$null | Out-Null
Write-Host 'Done.' -ForegroundColor Green
