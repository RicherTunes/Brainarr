# Download Lidarr DLLs for plugin development (Windows)

param(
    [string]$LidarrVersion = "2.12.4.4658",
    [string]$TargetPath = "lidarr-dlls"
)

Write-Host "Setting up Lidarr DLLs for plugin development..." -ForegroundColor Green

# Create target directory
if (Test-Path $TargetPath) {
    Write-Host "Cleaning existing Lidarr DLLs directory..." -ForegroundColor Yellow
    Remove-Item -Path $TargetPath -Recurse -Force
}
New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null

# Download Lidarr release
$DownloadUrl = "https://github.com/Lidarr/Lidarr/releases/download/v$LidarrVersion/Lidarr.master.$LidarrVersion.windows-core-x64.zip"
$DownloadPath = "lidarr-temp.zip"

Write-Host "Downloading Lidarr v$LidarrVersion..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $DownloadPath -UseBasicParsing
} catch {
    Write-Host "Failed to download Lidarr release. Please check the version number or try a different version." -ForegroundColor Red
    exit 1
}

# Extract archive
Write-Host "Extracting required DLLs..." -ForegroundColor Cyan
$TempExtractPath = "temp-extract"
Expand-Archive -Path $DownloadPath -DestinationPath $TempExtractPath -Force

# Copy required DLLs
$RequiredDLLs = @(
    "Lidarr.Core.dll",
    "Lidarr.Common.dll", 
    "Lidarr.Api.V1.dll",
    "Lidarr.Http.dll"
)

foreach ($dll in $RequiredDLLs) {
    $SourcePath = Join-Path $TempExtractPath "Lidarr\$dll"
    if (Test-Path $SourcePath) {
        Copy-Item -Path $SourcePath -Destination $TargetPath
        Write-Host "  ✓ $dll" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $dll not found in release" -ForegroundColor Red
    }
}

# Cleanup
Remove-Item -Path $DownloadPath -Force
Remove-Item -Path $TempExtractPath -Recurse -Force

$FullPath = (Resolve-Path $TargetPath).Path
Write-Host "`nLidarr DLLs setup complete in $TargetPath/" -ForegroundColor Green
Write-Host "Set LIDARR_PATH environment variable to: $FullPath" -ForegroundColor Yellow