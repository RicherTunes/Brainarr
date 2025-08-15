# Brainarr Production Build Script
# Handles version compatibility, dependency bundling, and CI/CD integration

param(
    [Parameter(Mandatory=$false)]
    [string]$LidarrTargetVersion = "2.13.1.4681",
    
    [Parameter(Mandatory=$false)]
    [string]$PluginVersion = "1.0.0",
    
    [Parameter(Mandatory=$false)]
    [string]$BuildNumber = "",
    
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release",
    
    [Parameter(Mandatory=$false)]
    [switch]$NoILRepack,
    
    [Parameter(Mandatory=$false)]
    [switch]$CI,
    
    [Parameter(Mandatory=$false)]
    [switch]$Clean,
    
    [Parameter(Mandatory=$false)]
    [switch]$Package,
    
    [Parameter(Mandatory=$false)]
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

# Script configuration
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = $ScriptRoot
$PluginProject = Join-Path $ProjectRoot "Brainarr.Plugin\Brainarr.Plugin.csproj"
$OutputDir = Join-Path $ProjectRoot "Build"
$PackageDir = Join-Path $ProjectRoot "Package"

# Build environment detection
if ($CI -or $env:GITHUB_ACTIONS -eq "true" -or $env:TF_BUILD -eq "True") {
    $BuildEnvironment = "CI"
    Write-Host "🏗️  Detected CI environment" -ForegroundColor Cyan
} else {
    $BuildEnvironment = "Local"
    Write-Host "💻  Local development build" -ForegroundColor Cyan
}

# Build number handling
if ([string]::IsNullOrEmpty($BuildNumber)) {
    if ($BuildEnvironment -eq "CI") {
        $BuildNumber = if ($env:GITHUB_RUN_NUMBER) { $env:GITHUB_RUN_NUMBER } else { (Get-Date -Format "yyyyMMdd") + "." + (Get-Random -Maximum 9999) }
    } else {
        $BuildNumber = "dev." + (Get-Date -Format "yyyyMMdd.HHmm")
    }
}

# Version calculation
if ($BuildEnvironment -eq "CI") {
    $FinalVersion = "$PluginVersion.$BuildNumber"
} else {
    $FinalVersion = "$PluginVersion-$BuildNumber"
}

Write-Host ""
Write-Host "🚀 Brainarr Production Build" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host "Plugin Version:    $FinalVersion" -ForegroundColor White
Write-Host "Assembly Version:  $LidarrTargetVersion" -ForegroundColor White  
Write-Host "Build Environment: $BuildEnvironment" -ForegroundColor White
Write-Host "Configuration:     $Configuration" -ForegroundColor White
Write-Host "ILRepack:          $(-not $NoILRepack)" -ForegroundColor White
Write-Host "Verbose:           $VerboseOutput" -ForegroundColor White
Write-Host ""

# Function to execute command with error handling
function Invoke-Command {
    param(
        [string]$Command,
        [string]$Description,
        [switch]$AllowFailure
    )
    
    Write-Host "▶️  $Description" -ForegroundColor Yellow
    if ($VerboseOutput) { Write-Host "   Command: $Command" -ForegroundColor Gray }
    
    try {
        $output = Invoke-Expression $Command 2>&1
        if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
            throw "Command failed with exit code $LASTEXITCODE"
        }
        if ($VerboseOutput -and $output) {
            Write-Host "   Output: $output" -ForegroundColor Gray
        }
        Write-Host "✅ $Description completed" -ForegroundColor Green
        return $output
    }
    catch {
        Write-Host "❌ $Description failed: $_" -ForegroundColor Red
        if (-not $AllowFailure) {
            throw
        }
        return $null
    }
}

# Validate prerequisites
Write-Host "🔍 Validating prerequisites..." -ForegroundColor Yellow

# Check for .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "❌ .NET SDK not found!" -ForegroundColor Red
    Write-Host "   Please install .NET 6.0 SDK or later" -ForegroundColor Yellow
    exit 1
}

# Check for ILRepack if enabled
if (-not $NoILRepack) {
    $ilrepackPath = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\ilrepack.lib.msbuild.task" -Recurse -Filter "ILRepack.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $ilrepackPath) {
        Write-Host "⚠️  ILRepack not found in NuGet cache - will be downloaded during build" -ForegroundColor Yellow
    } else {
        Write-Host "✅ ILRepack found: $($ilrepackPath.Directory)" -ForegroundColor Green
    }
}

# Check project file
if (-not (Test-Path $PluginProject)) {
    Write-Host "❌ Plugin project not found: $PluginProject" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Plugin project: $PluginProject" -ForegroundColor Green

# Clean if requested
if ($Clean) {
    Write-Host ""
    Write-Host "🧹 Cleaning build artifacts..." -ForegroundColor Yellow
    
    $DirsToClean = @($OutputDir, $PackageDir, "Brainarr.Plugin\bin", "Brainarr.Plugin\obj", "Brainarr.Tests\bin", "Brainarr.Tests\obj")
    foreach ($dir in $DirsToClean) {
        $fullPath = Join-Path $ProjectRoot $dir
        if (Test-Path $fullPath) {
            Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "   Cleaned: $dir" -ForegroundColor Gray
        }
    }
    Write-Host "✅ Clean completed" -ForegroundColor Green
}

# Create output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Build the plugin
Write-Host ""
Write-Host "🔨 Building Brainarr plugin..." -ForegroundColor Yellow

$buildProps = @(
    "Configuration=$Configuration"
    "BuildEnvironment=$BuildEnvironment"
    "LidarrTargetVersion=$LidarrTargetVersion"
    "PluginVersion=$PluginVersion"
    "BuildNumber=$BuildNumber"
    "ILRepackEnabled=$(-not $NoILRepack)"
)

$buildCommand = "dotnet build `"$PluginProject`" -c $Configuration --no-restore -v minimal " + 
    ($buildProps | ForEach-Object { "-p:$_" }) -join " "

try {
    # Restore packages first
    Invoke-Command -Command "dotnet restore `"$PluginProject`"" -Description "Restoring NuGet packages"
    
    # Build with custom properties
    Invoke-Command -Command $buildCommand -Description "Building plugin project"
    
    # Verify main plugin DLL was created
    $pluginDll = Get-ChildItem -Path "Brainarr.Plugin\bin\$Configuration" -Filter "Lidarr.Plugin.Brainarr.dll" -Recurse | Select-Object -First 1
    if (-not $pluginDll) {
        throw "Main plugin DLL not found after build"
    }
    
    # Copy to output directory
    $sourceDir = $pluginDll.Directory.FullName
    Copy-Item -Path "$sourceDir\*" -Destination $OutputDir -Recurse -Force
    
    Write-Host "✅ Build completed successfully" -ForegroundColor Green
    Write-Host "   Plugin DLL: $($pluginDll.Name) ($([math]::Round($pluginDll.Length / 1KB, 2)) KB)" -ForegroundColor Green
    
} catch {
    Write-Host "❌ Build failed: $_" -ForegroundColor Red
    exit 1
}

# Validate ILRepack results
if (-not $NoILRepack) {
    Write-Host ""
    Write-Host "🔍 Validating ILRepack results..." -ForegroundColor Yellow
    
    $mergedDll = Join-Path $OutputDir "Lidarr.Plugin.Brainarr.dll"
    if (Test-Path $mergedDll) {
        $dllInfo = Get-Item $mergedDll
        Write-Host "✅ Merged plugin DLL: $([math]::Round($dllInfo.Length / 1KB, 2)) KB" -ForegroundColor Green
        
        # Check if dependencies were merged (they should be gone)
        $dependencyFiles = @("Newtonsoft.Json.dll", "NLog.dll", "FluentValidation.dll", "Microsoft.Extensions.Caching.Memory.dll")
        $remainingDeps = $dependencyFiles | Where-Object { Test-Path (Join-Path $OutputDir $_) }
        
        if ($remainingDeps.Count -eq 0) {
            Write-Host "✅ All dependencies successfully merged" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Some dependencies not merged: $($remainingDeps -join ', ')" -ForegroundColor Yellow
        }
    } else {
        Write-Host "❌ Merged plugin DLL not found" -ForegroundColor Red
        exit 1
    }
}

# Package if requested
if ($Package) {
    Write-Host ""
    Write-Host "📦 Creating deployment package..." -ForegroundColor Yellow
    
    if (Test-Path $PackageDir) {
        Remove-Item -Path $PackageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
    
    # Copy main plugin DLL
    $pluginDll = Join-Path $OutputDir "Lidarr.Plugin.Brainarr.dll"
    if (Test-Path $pluginDll) {
        Copy-Item -Path $pluginDll -Destination $PackageDir
        Write-Host "   ✅ Packaged: Lidarr.Plugin.Brainarr.dll" -ForegroundColor Green
    }
    
    # Copy plugin manifest
    $pluginManifest = Join-Path $ProjectRoot "plugin.json"
    if (Test-Path $pluginManifest) {
        # Update version in manifest
        $manifestContent = Get-Content $pluginManifest -Raw | ConvertFrom-Json
        $manifestContent.version = $FinalVersion
        $manifestContent | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PackageDir "plugin.json") -Encoding UTF8
        Write-Host "   ✅ Packaged: plugin.json (version updated to $FinalVersion)" -ForegroundColor Green
    }
    
    # Copy any remaining required dependencies (non-merged)
    if ($NoILRepack) {
        $requiredDeps = @("Newtonsoft.Json.dll", "NLog.dll", "FluentValidation.dll", "Microsoft.Extensions.Caching.Memory.dll")
        foreach ($dep in $requiredDeps) {
            $depPath = Join-Path $OutputDir $dep
            if (Test-Path $depPath) {
                Copy-Item -Path $depPath -Destination $PackageDir
                Write-Host "   ✅ Packaged dependency: $dep" -ForegroundColor Green
            }
        }
    }
    
    # Create ZIP package
    $zipPath = Join-Path $ProjectRoot "Brainarr-$FinalVersion.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    
    try {
        Compress-Archive -Path "$PackageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
        $zipSize = [math]::Round((Get-Item $zipPath).Length / 1KB, 2)
        Write-Host "✅ Package created: Brainarr-$FinalVersion.zip ($zipSize KB)" -ForegroundColor Green
    } catch {
        Write-Host "❌ Failed to create ZIP package: $_" -ForegroundColor Red
    }
    
    # Create installation instructions
    $instructions = @"
BRAINARR PLUGIN INSTALLATION
============================

Version: $FinalVersion
Assembly Version: $LidarrTargetVersion
Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")
Build Environment: $BuildEnvironment

INSTALLATION INSTRUCTIONS:

1. STOP Lidarr service
2. Copy Lidarr.Plugin.Brainarr.dll to your Lidarr plugins directory:
   - Windows: C:\ProgramData\Lidarr\Plugins\
   - Linux: /opt/Lidarr/Plugins/
   - Docker: /config/Plugins/
3. Copy plugin.json to the same directory
4. RESTART Lidarr service
5. Go to Settings > Import Lists > Add Import List
6. Select "Brainarr AI Music Discovery"

REQUIREMENTS:
- Lidarr v2.13.1 or compatible runtime version
- .NET 6.0 runtime
- At least one AI provider configured

COMPATIBILITY:
- Built against Lidarr runtime: $LidarrTargetVersion
- Plugin version: $FinalVersion
- Dependencies: $( if ($NoILRepack) { "External" } else { "Bundled (ILRepack)" } )

TROUBLESHOOTING:
- Ensure Lidarr is stopped before installation
- Check Lidarr logs for any loading errors
- Verify plugin.json is in same directory as DLL
- For support, check GitHub repository

"@
    
    $instructions | Out-File -FilePath (Join-Path $PackageDir "INSTALL.txt") -Encoding UTF8
    Write-Host "   ✅ Created installation instructions" -ForegroundColor Green
}

# Build summary
Write-Host ""
Write-Host "🎉 Build Summary" -ForegroundColor Green
Write-Host "================" -ForegroundColor Green
Write-Host "Status:            SUCCESS" -ForegroundColor Green
Write-Host "Plugin Version:    $FinalVersion" -ForegroundColor White
Write-Host "Assembly Version:  $LidarrTargetVersion" -ForegroundColor White
Write-Host "Build Environment: $BuildEnvironment" -ForegroundColor White
Write-Host "Output Directory:  $OutputDir" -ForegroundColor White

if ($Package) {
    Write-Host "Package Directory: $PackageDir" -ForegroundColor White
    Write-Host "Package File:      Brainarr-$FinalVersion.zip" -ForegroundColor White
}

Write-Host ""
Write-Host "✅ Production build completed successfully!" -ForegroundColor Green
Write-Host ""

# CI/CD integration outputs
if ($BuildEnvironment -eq "CI") {
    Write-Host "##[set-output name=plugin-version;]$FinalVersion"
    Write-Host "##[set-output name=assembly-version;]$LidarrTargetVersion"
    Write-Host "##[set-output name=package-path;]$(Join-Path $ProjectRoot "Brainarr-$FinalVersion.zip")"
}