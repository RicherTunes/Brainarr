param(
    [switch]$Setup,
    [switch]$Test,
    [switch]$Package,
    [switch]$Clean,
    [switch]$Deploy,
    [string]$Configuration = "Release",
    [string]$DeployPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Brainarr"
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

# Convert to absolute path for build process
$env:LIDARR_PATH = (Get-Item $env:LIDARR_PATH).FullName

# Clean if requested
if ($Clean) {
    Write-Host "`nCleaning build artifacts..." -ForegroundColor Green
    
    # Clean bin and obj directories
    $cleanPaths = @(
        ".\Brainarr.Plugin\bin",
        ".\Brainarr.Plugin\obj",
        ".\Brainarr.Tests\bin",
        ".\Brainarr.Tests\obj"
    )
    
    foreach ($path in $cleanPaths) {
        if (Test-Path $path) {
            Write-Host "Removing: $path" -ForegroundColor Yellow
            Remove-Item $path -Recurse -Force
        }
    }
    
    # Clean any existing packages
    Get-ChildItem -Path . -Name "Brainarr-v*.zip" | ForEach-Object {
        Write-Host "Removing package: $_" -ForegroundColor Yellow
        Remove-Item $_
    }
    
    Write-Host "Clean completed!" -ForegroundColor Green
}

# Build the plugin  
Write-Host "`nBuilding Brainarr plugin ($Configuration)..." -ForegroundColor Green
Write-Host "Using Lidarr assemblies from: $env:LIDARR_PATH" -ForegroundColor Cyan

Push-Location ".\Brainarr.Plugin"
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed"
    }
    
    # Build with explicit Lidarr path parameter
    dotnet build -c $Configuration -p:LidarrPath="$env:LIDARR_PATH"
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
    $outputPath = ".\Brainarr.Plugin\bin"
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
        
        # Create package with only essential plugin files
        $filesToPackage = @(
            "Lidarr.Plugin.Brainarr.dll",
            "plugin.json"
        )
        
        # Add debug symbols if available  
        if (Test-Path "Lidarr.Plugin.Brainarr.pdb") {
            $filesToPackage += "Lidarr.Plugin.Brainarr.pdb"
        }
        
        Compress-Archive -Path $filesToPackage -DestinationPath "..\..\..\..\$packageName" -Force -CompressionLevel Optimal
        
        Write-Host "Package created: $packageName" -ForegroundColor Green
    } finally {
        Pop-Location
    }
}

# Deploy if requested
if ($Deploy) {
    Write-Host "`nDeploying plugin..." -ForegroundColor Green
    
    # Check if deploy path exists, create if not
    if (!(Test-Path $DeployPath)) {
        Write-Host "Creating deploy directory: $DeployPath" -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
    }
    
    # Check if we have built plugin files
    $pluginDll = ".\Brainarr.Plugin\bin\Lidarr.Plugin.Brainarr.dll"
    $pluginJson = ".\Brainarr.Plugin\bin\plugin.json"
    
    if (!(Test-Path $pluginDll)) {
        Write-Host "Plugin DLL not found! Run build first." -ForegroundColor Red
        exit 1
    }
    
    if (!(Test-Path $pluginJson)) {
        Write-Host "Plugin manifest not found! Run build first." -ForegroundColor Red
        exit 1
    }
    
    # Copy plugin files to deploy directory
    Write-Host "Copying plugin files to: $DeployPath" -ForegroundColor Cyan
    
    # Copy main plugin DLL
    Copy-Item $pluginDll $DeployPath -Force
    Write-Host "  Copied: Lidarr.Plugin.Brainarr.dll" -ForegroundColor Green
    
    # Copy plugin manifest
    Copy-Item $pluginJson $DeployPath -Force
    Write-Host "  Copied: plugin.json" -ForegroundColor Green
    
    # Copy debug symbols if available
    $pluginPdb = ".\Brainarr.Plugin\bin\Lidarr.Plugin.Brainarr.pdb"
    if (Test-Path $pluginPdb) {
        Copy-Item $pluginPdb $DeployPath -Force
        Write-Host "  Copied: Lidarr.Plugin.Brainarr.pdb (debug symbols)" -ForegroundColor Green
    }
    
    Write-Host "`nDeployment completed to: $DeployPath" -ForegroundColor Green
    Write-Host "Restart Lidarr to load the updated plugin." -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Green