[CmdletBinding()]
param(
    [switch]$SkipLidarrSetup,
    [switch]$SkipBuild,
    [switch]$SkipRestore,
    [switch]$RunTests,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Display
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Host "$Display is required but was not found in PATH." -ForegroundColor Red
        throw "$Display is required."
    }
}

function Resolve-LidarrPath {
    param([string]$Root)

    $candidates = @()
    if ($env:LIDARR_PATH) {
        $candidates += $env:LIDARR_PATH
    }

    $marker = Join-Path $Root "ext/lidarr-path.txt"
    if (Test-Path $marker) {
        $candidates += (Get-Content -Path $marker -ErrorAction SilentlyContinue)
    }

    $candidates += @(
        Join-Path $Root "ext/Lidarr/_output/net6.0",
        Join-Path $Root "ext/Lidarr/src/Lidarr/bin/Release/net6.0"
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            $expanded = $candidate
            $resolved = Resolve-Path -Path $expanded -ErrorAction SilentlyContinue
            if ($resolved) {
                $expanded = $resolved.ProviderPath
            }

            if (Test-Path (Join-Path $expanded "Lidarr.Core.dll")) {
                return (Get-Item $expanded).FullName
            }
        }
    }

    return $null
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot
if (-not (Test-Path "./Brainarr.sln")) {
    throw "Run setup.ps1 from the repository root (Brainarr.sln missing)."
}

try {
    Assert-Command -Name "dotnet" -Display ".NET SDK"
    Assert-Command -Name "git" -Display "git"

    if (-not $SkipLidarrSetup) {
        Write-Host "Running Lidarr setup..." -ForegroundColor Green
        & (Join-Path $scriptRoot "setup-lidarr.ps1") | Out-Null
    }

    $lidarrPath = Resolve-LidarrPath -Root $scriptRoot
    if (-not $lidarrPath) {
        throw "Could not locate Lidarr assemblies. Run setup-lidarr.ps1 first or set LIDARR_PATH."
    }

    $env:LIDARR_PATH = $lidarrPath
    Write-Host "Using Lidarr assemblies from: $lidarrPath" -ForegroundColor Cyan

    if (-not $SkipRestore) {
        Write-Host "Restoring solution packages..." -ForegroundColor Green
        dotnet restore ./Brainarr.sln
        if ($LASTEXITCODE -ne 0) {
            throw "Restore failed."
        }
    }

    if (-not $SkipBuild) {
        Write-Host "Building Brainarr plugin ($Configuration)..." -ForegroundColor Green
        dotnet build ./Brainarr.sln -c $Configuration -p:LidarrPath="$lidarrPath"
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed."
        }
    }

    if ($RunTests) {
        if (-not (Test-Path "./Brainarr.Tests/Brainarr.Tests.csproj")) {
            Write-Host "Tests project not found; skipping tests." -ForegroundColor Yellow
        }
        else {
            Write-Host "Running tests..." -ForegroundColor Green
            dotnet test ./Brainarr.Tests/Brainarr.Tests.csproj -c $Configuration --no-build
            if ($LASTEXITCODE -ne 0) {
                throw "Tests failed."
            }
        }
    }

    Write-Host "Setup complete. You are ready to work on Brainarr." -ForegroundColor Green
}
finally {
    Pop-Location
}
