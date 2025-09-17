[CmdletBinding()]
param(
    [string]$Branch = "plugins",
    [string]$ExtPath = "./ext/Lidarr",
    [switch]$SkipBuild
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

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $scriptRoot
if (-not (Test-Path (Join-Path $scriptRoot "Brainarr.sln"))) {
    throw "Run setup-lidarr.ps1 from the repository root (Brainarr.sln missing)."
}

try {
    Assert-Command -Name "git" -Display "git"
    Assert-Command -Name "dotnet" -Display ".NET SDK"

    if (-not (Test-Path "./ext")) {
        New-Item -ItemType Directory -Path "./ext" -Force | Out-Null
    }

    $extFullPath = if ([System.IO.Path]::IsPathRooted($ExtPath)) { $ExtPath } else { Join-Path $scriptRoot $ExtPath }
    $resolved = Resolve-Path -Path $extFullPath -ErrorAction SilentlyContinue
    if ($resolved) {
        $extFullPath = $resolved.ProviderPath
    }

    $extParent = Split-Path -Parent $extFullPath
    if (-not (Test-Path $extParent)) {
        New-Item -ItemType Directory -Path $extParent -Force | Out-Null
    }

    if (-not (Test-Path $extFullPath)) {
        Write-Host "Cloning Lidarr repository (branch: $Branch)..." -ForegroundColor Yellow
        git clone --branch $Branch --depth 1 https://github.com/Lidarr/Lidarr.git $extFullPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to clone Lidarr repository."
        }
    }
    else {
        Write-Host "Updating existing Lidarr checkout..." -ForegroundColor Yellow
        Push-Location $extFullPath
        try {
            git fetch origin $Branch
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to fetch Lidarr updates."
            }
            git reset --hard origin/$Branch
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to reset Lidarr checkout."
            }
        }
        finally {
            Pop-Location
        }
    }

    $lidarrBinPath = $null

    if (-not $SkipBuild) {
        Write-Host "Building Lidarr..." -ForegroundColor Yellow
        Push-Location (Join-Path $extFullPath "src")
        try {
            dotnet restore Lidarr.sln
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to restore Lidarr packages."
            }

            dotnet build Lidarr.sln -c Release
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to build Lidarr."
            }

            Write-Host "Lidarr built successfully." -ForegroundColor Green
        }
        finally {
            Pop-Location
        }
    }

    $lidarrCandidates = @(
        Join-Path $extFullPath "_output/net6.0",
        Join-Path $extFullPath "src/Lidarr/bin/Release/net6.0"
    )

    foreach ($candidate in $lidarrCandidates) {
        if ($candidate -and (Test-Path (Join-Path $candidate "Lidarr.Core.dll"))) {
            $lidarrBinPath = $candidate
            break
        }
    }

    if (-not $lidarrBinPath) {
        Write-Host "Warning: Lidarr build output was not found. Ensure the build succeeded." -ForegroundColor Yellow
        return
    }

    $lidarrBinPath = (Get-Item $lidarrBinPath).FullName
    $env:LIDARR_PATH = $lidarrBinPath
    [Environment]::SetEnvironmentVariable("LIDARR_PATH", $lidarrBinPath, "Process")
    try {
        [Environment]::SetEnvironmentVariable("LIDARR_PATH", $lidarrBinPath, "User")
    }
    catch {
        Write-Host "Note: Unable to persist LIDARR_PATH for the current user." -ForegroundColor Yellow
    }

    $markerFile = Join-Path $scriptRoot "ext/lidarr-path.txt"
    New-Item -ItemType Directory -Path (Split-Path -Parent $markerFile) -Force | Out-Null
    Set-Content -Path $markerFile -Value $lidarrBinPath

    Write-Host "Set LIDARR_PATH to: $lidarrBinPath" -ForegroundColor Green
    Write-Host "Path recorded at ext/lidarr-path.txt" -ForegroundColor Green
    Write-Host "You may need to restart your terminal for persistent environment changes to take effect." -ForegroundColor Yellow

    Write-Output $lidarrBinPath
}
finally {
    Pop-Location
}

Write-Host "Setup complete." -ForegroundColor Green
Write-Host "Next: run ./setup.ps1 or ./setup.sh to restore and build Brainarr." -ForegroundColor Cyan
