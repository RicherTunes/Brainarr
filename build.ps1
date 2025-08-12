param(
    [switch]$Setup,
    [switch]$Test,
    [switch]$Package,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Setup Lidarr if requested
if ($Setup) {
    Write-Host "Running Lidarr setup..." -ForegroundColor Green
    & ".\setup-lidarr.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Setup failed!" -ForegroundColor Red
        exit 1
    }
}

# Check if we have Lidarr available
$lidarrPaths = @(
    ".\ext\Lidarr\_output\net6.0",
    ".\ext\Lidarr\src\Lidarr\bin\Release\net6.0",
    $env:LIDARR_PATH,
    "C:\ProgramData\Lidarr\bin"
)

$foundLidarr = $false
foreach ($path in $lidarrPaths) {
    if ($path -and (Test-Path "$path\Lidarr.Core.dll" -ErrorAction SilentlyContinue)) {
        Write-Host "Found Lidarr at: $path" -ForegroundColor Green
        $env:LIDARR_PATH = $path
        $foundLidarr = $true
        break
    }
}

if (!$foundLidarr) {
    Write-Host "Lidarr not found! Run with -Setup flag to clone and build Lidarr." -ForegroundColor Red
    Write-Host "Example: .\build.ps1 -Setup" -ForegroundColor Yellow
    exit 1
}

# Build the plugin
Write-Host "`nBuilding Brainarr plugin ($Configuration)..." -ForegroundColor Green
Push-Location ".\Brainarr.Plugin"
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed"
    }
    
    dotnet build -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    Write-Host "Build successful!" -ForegroundColor Green
} finally {
    Pop-Location
}

# Run tests if requested
if ($Test) {
    Write-Host "`nRunning tests..." -ForegroundColor Green
    if (Test-Path ".\Brainarr.Tests") {
        Push-Location ".\Brainarr.Tests"
        try {
            dotnet test -c $Configuration --no-build
            if ($LASTEXITCODE -ne 0) {
                throw "Tests failed"
            }
            Write-Host "Tests passed!" -ForegroundColor Green
        } finally {
            Pop-Location
        }
    } else {
        Write-Host "No test project found" -ForegroundColor Yellow
    }
}

# Package if requested
if ($Package) {
    Write-Host "`nPackaging plugin..." -ForegroundColor Green
    $outputPath = ".\Brainarr.Plugin\bin\$Configuration\net6.0"
    if (!(Test-Path $outputPath)) {
        Write-Host "Build output not found at: $outputPath" -ForegroundColor Red
        exit 1
    }
    
    $version = "1.0.0"
    $packageName = "Brainarr-v$version.zip"
    
    Push-Location $outputPath
    try {
        # Remove existing package
        if (Test-Path "..\..\..\..\$packageName") {
            Remove-Item "..\..\..\..\$packageName"
        }
        
        # Create package (exclude Lidarr and debug files)
        Compress-Archive -Path * -DestinationPath "..\..\..\..\$packageName" -Force `
            -CompressionLevel Optimal `
            | Where-Object { $_.Name -notlike "*.pdb" -and $_.Name -notlike "Lidarr.*" -and $_.Name -notlike "NzbDrone.*" }
        
        Write-Host "Package created: $packageName" -ForegroundColor Green
    } finally {
        Pop-Location
    }
}

Write-Host "`nDone!" -ForegroundColor Green