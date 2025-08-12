# Setup script to clone and prepare Lidarr for plugin development
param(
    [string]$Branch = "plugins",
    [string]$ExtPath = ".\ext\Lidarr"
)

Write-Host "Setting up Lidarr source for plugin development..." -ForegroundColor Green

# Create external directory if it doesn't exist
if (!(Test-Path ".\ext")) {
    New-Item -ItemType Directory -Path ".\ext" | Out-Null
    Write-Host "Created ext directory"
}

# Clone or update Lidarr
if (!(Test-Path $ExtPath)) {
    Write-Host "Cloning Lidarr repository (branch: $Branch)..." -ForegroundColor Yellow
    git clone --branch $Branch --depth 1 https://github.com/Lidarr/Lidarr.git $ExtPath
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to clone Lidarr repository" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Updating existing Lidarr checkout..." -ForegroundColor Yellow
    Push-Location $ExtPath
    git fetch origin $Branch
    git reset --hard origin/$Branch
    Pop-Location
}

# Build Lidarr
Write-Host "Building Lidarr..." -ForegroundColor Yellow
Push-Location $ExtPath
try {
    # Restore packages
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restore Lidarr packages"
    }
    
    # Build Lidarr
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build Lidarr"
    }
    
    Write-Host "Lidarr built successfully!" -ForegroundColor Green
    
    # Set environment variable for plugin build
    $lidarrBinPath = Join-Path (Get-Location) "_output\net6.0"
    if (!(Test-Path $lidarrBinPath)) {
        $lidarrBinPath = Join-Path (Get-Location) "src\Lidarr\bin\Release\net6.0"
    }
    
    if (Test-Path $lidarrBinPath) {
        [Environment]::SetEnvironmentVariable("LIDARR_PATH", $lidarrBinPath, "User")
        Write-Host "Set LIDARR_PATH to: $lidarrBinPath" -ForegroundColor Green
        Write-Host "You may need to restart your terminal for the environment variable to take effect" -ForegroundColor Yellow
    } else {
        Write-Host "Warning: Could not find Lidarr build output directory" -ForegroundColor Yellow
    }
} finally {
    Pop-Location
}

Write-Host "`nSetup complete!" -ForegroundColor Green
Write-Host "You can now build the Brainarr plugin with: dotnet build -c Release" -ForegroundColor Cyan