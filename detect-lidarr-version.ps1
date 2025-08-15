# Lidarr Version Detection Script
# Automatically detects installed Lidarr version and recommends compatible AssemblyVersion

param(
    [Parameter(Mandatory=$false)]
    [string]$LidarrPath = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$AutoDetect,
    
    [Parameter(Mandatory=$false)]
    [switch]$ShowAll
)

$ErrorActionPreference = "SilentlyContinue"

Write-Host "Lidarr Version Detection for Brainarr Plugin" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Function to get assembly version from DLL
function Get-AssemblyVersion {
    param([string]$FilePath)
    
    try {
        $assembly = [System.Reflection.Assembly]::LoadFile($FilePath)
        return $assembly.GetName().Version.ToString()
    } catch {
        try {
            # Fallback to file version
            $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($FilePath)
            return "$($version.FileMajorPart).$($version.FileMinorPart).$($version.FileBuildPart).$($version.FilePrivatePart)"
        } catch {
            return "Unknown"
        }
    }
}

# Function to check if a Lidarr installation exists at path
function Test-LidarrInstallation {
    param([string]$Path)
    
    $lidarrExe = Join-Path $Path "Lidarr.exe"
    $lidarrDll = Join-Path $Path "Lidarr.Console.dll"
    $coreAssembly = Join-Path $Path "Lidarr.Core.dll"
    
    return (Test-Path $lidarrExe) -or (Test-Path $lidarrDll) -or (Test-Path $coreAssembly)
}

# Common Lidarr installation paths
$commonPaths = @(
    "C:\Program Files\Lidarr\bin",
    "C:\Program Files (x86)\Lidarr\bin", 
    "C:\ProgramData\Lidarr\bin",
    "$env:LOCALAPPDATA\Lidarr\bin",
    "$env:APPDATA\Lidarr\bin",
    "/opt/Lidarr/bin",
    "/usr/local/bin/Lidarr",
    "/app/bin",  # Docker
    "/usr/lib/lidarr/bin"
)

# Docker-specific paths
$dockerPaths = @(
    "/config",
    "/app",
    "/usr/lib/lidarr"
)

$foundInstallations = @()

# Check specified path first
if ($LidarrPath -and (Test-Path $LidarrPath)) {
    if (Test-LidarrInstallation $LidarrPath) {
        $foundInstallations += @{
            Path = $LidarrPath
            Type = "Specified"
        }
    } else {
        Write-Host "No Lidarr installation found at specified path: $LidarrPath" -ForegroundColor Red
    }
}

# Auto-detect installations
if ($AutoDetect -or [string]::IsNullOrEmpty($LidarrPath)) {
    Write-Host "Scanning for Lidarr installations..." -ForegroundColor Yellow
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            if (Test-LidarrInstallation $path) {
                $foundInstallations += @{
                    Path = $path
                    Type = "Auto-detected"
                }
            }
        }
    }
    
    # Check for Docker installations
    if ($env:DOCKER_CONTAINER -eq "true" -or (Test-Path "/.dockerenv")) {
        foreach ($path in $dockerPaths) {
            if (Test-Path $path) {
                if (Test-LidarrInstallation $path) {
                    $foundInstallations += @{
                        Path = $path
                        Type = "Docker"
                    }
                }
            }
        }
    }
}

if ($foundInstallations.Count -eq 0) {
    Write-Host "No Lidarr installations found" -ForegroundColor Red
    Write-Host ""
    Write-Host "Manual installation detection:" -ForegroundColor Yellow
    Write-Host "   Run: .\detect-lidarr-version.ps1 -LidarrPath 'C:\Path\To\Lidarr\bin'" -ForegroundColor White
    Write-Host ""
    Write-Host "For plugin development without Lidarr installed:" -ForegroundColor Yellow
    Write-Host "   Use default compatible version: 2.13.1.4681" -ForegroundColor White
    exit 0
}

Write-Host "Found $($foundInstallations.Count) Lidarr installation(s)" -ForegroundColor Green
Write-Host ""

$recommendedVersions = @()

foreach ($installation in $foundInstallations) {
    $path = $installation.Path
    $type = $installation.Type
    
    Write-Host "$type Installation: $path" -ForegroundColor Cyan
    
    # Check for key assemblies
    $assemblies = @(
        @{ Name = "Lidarr.Core.dll"; Purpose = "Core runtime" },
        @{ Name = "Lidarr.Common.dll"; Purpose = "Common libraries" },
        @{ Name = "Lidarr.Console.dll"; Purpose = "Console host" },
        @{ Name = "Lidarr.exe"; Purpose = "Main executable" }
    )
    
    $versions = @{}
    
    foreach ($assembly in $assemblies) {
        $assemblyPath = Join-Path $path $assembly.Name
        if (Test-Path $assemblyPath) {
            $version = Get-AssemblyVersion $assemblyPath
            $versions[$assembly.Name] = $version
            
            if ($ShowAll) {
                Write-Host "   $($assembly.Name): $version ($($assembly.Purpose))" -ForegroundColor Gray
            }
        }
    }
    
    # Determine the runtime version (typically from Lidarr.Core.dll)
    $runtimeVersion = $null
    if ($versions["Lidarr.Core.dll"] -and $versions["Lidarr.Core.dll"] -ne "Unknown") {
        $runtimeVersion = $versions["Lidarr.Core.dll"]
    } elseif ($versions["Lidarr.Common.dll"] -and $versions["Lidarr.Common.dll"] -ne "Unknown") {
        $runtimeVersion = $versions["Lidarr.Common.dll"]  
    } elseif ($versions["Lidarr.Console.dll"] -and $versions["Lidarr.Console.dll"] -ne "Unknown") {
        $runtimeVersion = $versions["Lidarr.Console.dll"]
    }
    
    if ($runtimeVersion) {
        Write-Host "   Runtime Version: $runtimeVersion" -ForegroundColor Green
        
        # Map to compatible plugin AssemblyVersion
        $compatibleVersion = $runtimeVersion
        
        # Special handling for known version mappings
        if ($runtimeVersion.StartsWith("2.13.2")) {
            $compatibleVersion = "2.13.1.4681"  # Use working version for 2.13.2.x
        } elseif ($runtimeVersion.StartsWith("2.13.1")) {
            $compatibleVersion = $runtimeVersion  # Direct match
        } elseif ($runtimeVersion.StartsWith("10.0.0") -or $runtimeVersion.StartsWith("1.0.")) {
            $compatibleVersion = "2.13.1.4681"  # Plugins branch -> runtime mapping
        }
        
        Write-Host "   Recommended Plugin AssemblyVersion: $compatibleVersion" -ForegroundColor Yellow
        
        $recommendedVersions += @{
            InstallationPath = $path
            RuntimeVersion = $runtimeVersion
            RecommendedAssemblyVersion = $compatibleVersion
            Type = $type
        }
    } else {
        Write-Host "   ❌ Could not determine runtime version" -ForegroundColor Red
    }
    
    Write-Host ""
}

# Summary and recommendations
if ($recommendedVersions.Count -gt 0) {
    Write-Host "📋 Build Recommendations" -ForegroundColor Green
    Write-Host "========================" -ForegroundColor Green
    Write-Host ""
    
    # Group by recommended version
    $versionGroups = $recommendedVersions | Group-Object RecommendedAssemblyVersion
    
    foreach ($group in $versionGroups) {
        $version = $group.Name
        $installations = $group.Group
        
        Write-Host "For AssemblyVersion $version`:" -ForegroundColor Cyan
        foreach ($install in $installations) {
            Write-Host "   ✅ Compatible with: $($install.Type) at $($install.InstallationPath)" -ForegroundColor Green
            Write-Host "      Runtime: $($install.RuntimeVersion)" -ForegroundColor Gray
        }
        Write-Host ""
        
        # Build command recommendations
        Write-Host "Build Commands:" -ForegroundColor Yellow
        Write-Host "   Local build:" -ForegroundColor White
        Write-Host "   .\build-production.ps1 -LidarrTargetVersion '$version'" -ForegroundColor Gray
        Write-Host ""
        Write-Host "   CI build:" -ForegroundColor White  
        Write-Host "   env LIDARR_TARGET_VERSION='$version' .\build-production.ps1 -CI" -ForegroundColor Gray
        Write-Host ""
    }
    
    # Primary recommendation (most common version)
    $primaryVersion = ($versionGroups | Sort-Object Count -Descending | Select-Object -First 1).Name
    
    Write-Host "Primary Recommendation: $primaryVersion" -ForegroundColor Green
    Write-Host "   This version is compatible with the most installations found" -ForegroundColor Gray
    Write-Host ""
    
    # Update Directory.Build.props suggestion
    $buildPropsPath = Join-Path (Split-Path $MyInvocation.MyCommand.Path) "Directory.Build.props"
    if (Test-Path $buildPropsPath) {
        Write-Host "To set as default in Directory.Build.props:" -ForegroundColor Yellow
        Write-Host "   Update <LidarrTargetVersion> to: $primaryVersion" -ForegroundColor White
        Write-Host ""
    }
} else {
    Write-Host "❌ No compatible versions could be determined" -ForegroundColor Red
    Write-Host ""
    Write-Host "💡 Fallback recommendations:" -ForegroundColor Yellow
    Write-Host "   • For current Lidarr stable: 2.13.1.4681" -ForegroundColor White
    Write-Host "   • For development/testing: 2.13.2.4686" -ForegroundColor White
}

# Plugin compatibility notes
Write-Host "ℹ️  Plugin Compatibility Notes" -ForegroundColor Blue
Write-Host "==============================" -ForegroundColor Blue
Write-Host "• Plugin AssemblyVersion must match Lidarr runtime for loading" -ForegroundColor White
Write-Host "• Our build system automatically handles version targeting" -ForegroundColor White
Write-Host "• ILRepack bundles dependencies to avoid version conflicts" -ForegroundColor White
Write-Host "• Use detected version for maximum compatibility" -ForegroundColor White
Write-Host ""