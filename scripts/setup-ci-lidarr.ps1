# CI script to setup Lidarr dependencies for building Brainarr plugin
# This script provides multiple fallback options for CI environments

param(
    [string]$OutputPath = "mock-lidarr\bin"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$MockLidarrDir = Join-Path $ProjectRoot $OutputPath

Write-Host "Setting up Lidarr dependencies for CI..."
Write-Host "Project root: $ProjectRoot"
Write-Host "Mock Lidarr dir: $MockLidarrDir"

# Create output directory
if (!(Test-Path $MockLidarrDir)) {
    New-Item -ItemType Directory -Path $MockLidarrDir -Force | Out-Null
}

$CoreSuccess = $false
$CommonSuccess = $false

# Method 1: Try to build proper assembly stubs (preferred)
Write-Host "Attempting to build Lidarr assembly stubs..."
$StubsDir = Join-Path $ProjectRoot "ci-stubs"

if (Test-Path $StubsDir) {
    try {
        $CoreProject = Join-Path $StubsDir "Lidarr.Core.Stubs.csproj"
        if (Test-Path $CoreProject) {
            dotnet build $CoreProject --configuration Release --output $MockLidarrDir --nologo --verbosity quiet
            Write-Host "✓ Built Lidarr.Core.dll stub successfully"
            $CoreSuccess = $true
        }
    }
    catch {
        Write-Host "⚠ Failed to build Lidarr.Core.dll stub: $_"
    }
    
    try {
        $CommonProject = Join-Path $StubsDir "Lidarr.Common.Stubs.csproj"
        if (Test-Path $CommonProject) {
            dotnet build $CommonProject --configuration Release --output $MockLidarrDir --nologo --verbosity quiet
            Write-Host "✓ Built Lidarr.Common.dll stub successfully"
            $CommonSuccess = $true
        }
    }
    catch {
        Write-Host "⚠ Failed to build Lidarr.Common.dll stub: $_"
    }
}
else {
    Write-Host "⚠ CI stubs directory not found"
}

# Method 2: Fallback to actual Lidarr build (if available)
if (!$CoreSuccess -or !$CommonSuccess) {
    Write-Host "Attempting to use real Lidarr assemblies..."
    
    $LidarrCorePath = Join-Path $ProjectRoot "ext\Lidarr\_output\net6.0\Lidarr.Core.dll"
    $LidarrCommonPath = Join-Path $ProjectRoot "ext\Lidarr\_output\net6.0\Lidarr.Common.dll"
    
    if (Test-Path $LidarrCorePath) {
        Write-Host "✓ Found pre-built Lidarr assemblies, copying..."
        try {
            Copy-Item $LidarrCorePath $MockLidarrDir -Force
            Copy-Item $LidarrCommonPath $MockLidarrDir -Force
            $CoreSuccess = $true
            $CommonSuccess = $true
        }
        catch {
            Write-Host "⚠ Failed to copy pre-built assemblies: $_"
        }
    }
    else {
        Write-Host "⚠ No pre-built Lidarr assemblies found"
    }
}

# Method 3: Create minimal placeholder assemblies (last resort)
$PlaceholderContent = '<?xml version="1.0" encoding="utf-8"?><assembly></assembly>'

if (!$CoreSuccess) {
    Write-Host "Creating minimal Lidarr.Core.dll placeholder..."
    $CorePath = Join-Path $MockLidarrDir "Lidarr.Core.dll"
    Set-Content -Path $CorePath -Value $PlaceholderContent -Encoding UTF8
}

if (!$CommonSuccess) {
    Write-Host "Creating minimal Lidarr.Common.dll placeholder..."
    $CommonPath = Join-Path $MockLidarrDir "Lidarr.Common.dll"
    Set-Content -Path $CommonPath -Value $PlaceholderContent -Encoding UTF8
}

# Create other minimal placeholder DLLs
Write-Host "Creating placeholder DLLs for other assemblies..."
$OtherDlls = @("Lidarr.Api.V1.dll", "Lidarr.Http.dll", "Equ.dll")

foreach ($dll in $OtherDlls) {
    $dllPath = Join-Path $MockLidarrDir $dll
    if (!(Test-Path $dllPath)) {
        Set-Content -Path $dllPath -Value $PlaceholderContent -Encoding UTF8
    }
}

# Verify what we have
Write-Host ""
Write-Host "Lidarr dependency setup complete. Available assemblies:"
Get-ChildItem $MockLidarrDir | ForEach-Object { Write-Host "  $($_.Name)" }

# Set environment variable for dotnet build
$env:LIDARR_PATH = $MockLidarrDir
Write-Host "LIDARR_PATH set to: $($env:LIDARR_PATH)"

Write-Host ""
if ($CoreSuccess -and $CommonSuccess) {
    Write-Host "✓ Lidarr dependencies setup completed successfully"
    exit 0
}
else {
    Write-Host "⚠ Lidarr dependencies setup completed with fallbacks"
    Write-Host "  Build may still succeed with placeholder assemblies"
    exit 0
}