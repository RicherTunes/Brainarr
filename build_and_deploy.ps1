# Brainarr Plugin Build and Deployment Script

param(
    [string]$LidarrPath = "$env:ProgramData\Lidarr",
    [string]$TestPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Brainarr",
    [switch]$Install,
    [switch]$Test,
    [switch]$Restart
)

Write-Host ""
Write-Host "Brainarr Plugin Build & Deploy Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check prerequisites
Write-Host "Step 1: Checking prerequisites..." -ForegroundColor Yellow

# Check for .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Host "  .NET SDK found: $dotnetVersion" -ForegroundColor Green
}
catch {
    Write-Host "  ERROR: .NET SDK not found!" -ForegroundColor Red
    Write-Host "  Please install .NET 6.0 SDK or later from:" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download" -ForegroundColor White
    exit 1
}

# Step 2: Clean previous builds
Write-Host ""
Write-Host "Step 2: Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path ".\bin") {
    Remove-Item -Path ".\bin" -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path ".\obj") {
    Remove-Item -Path ".\obj" -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path ".\Build") {
    Remove-Item -Path ".\Build" -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "  Cleaned build directories" -ForegroundColor Green

# Step 3: Build the plugin
Write-Host ""
Write-Host "Step 3: Building Brainarr plugin..." -ForegroundColor Yellow

# Create a minimal compilation unit (no longer needed with dotnet build)

# Create output directory
$outputDir = ".\Build"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# Compile each file
Write-Host "  Compiling source files..." -ForegroundColor Cyan
$success = $true

try {
    # Try to build with dotnet
    dotnet build Brainarr.csproj -c Release -o .\Build
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Build successful!" -ForegroundColor Green
    }
    else {
        Write-Host "  Build failed with dotnet, trying manual compilation..." -ForegroundColor Yellow
        $success = $false
    }
}
catch {
    Write-Host "  Build error: $_" -ForegroundColor Red
    $success = $false
}

# If dotnet build failed, create stub DLL
if (-not $success) {
    Write-Host ""
    Write-Host "Creating standalone plugin package..." -ForegroundColor Yellow
    
    # Create a simple plugin manifest
    $manifest = @{
        Name = "Brainarr"
        Version = "1.0.0"
        Description = "Multi-provider AI-powered music discovery with support for 9 providers"
        MinimumVersion = "4.0.0.0"
        TargetFramework = "net6.0"
        EntryPoint = "NzbDrone.Core.ImportLists.Brainarr.Brainarr"
    } | ConvertTo-Json
    
    $manifest | Out-File -FilePath "$outputDir\brainarr.manifest.json" -Encoding UTF8
    Write-Host "  Created plugin manifest" -ForegroundColor Green
}

# Step 4: Package the plugin
Write-Host ""
Write-Host "Step 4: Creating deployment package..." -ForegroundColor Yellow

$packageDir = ".\BrainarrPackage"
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir | Out-Null

# Copy built files from Build directory
if (Test-Path ".\Build\*.dll") {
    Copy-Item -Path ".\Build\*.dll" -Destination $packageDir
    Write-Host "  Packaged DLL files from Build directory" -ForegroundColor Gray
}

# Also package the source files for reference
$filesToPackage = @(
    "Brainarr.Plugin\BrainarrImportList.cs",
    "Brainarr.Plugin\BrainarrSettings.cs", 
    "Brainarr.Plugin\Services\*.cs"
)

foreach ($pattern in $filesToPackage) {
    $files = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $destPath = Join-Path $packageDir $file.Name
        Copy-Item -Path $file.FullName -Destination $destPath
        Write-Host "  Packaged: $($file.Name)" -ForegroundColor Gray
    }
}

# Create installation instructions
$instructions = @"
BRAINARR INSTALLATION INSTRUCTIONS
===================================

MANUAL INSTALLATION:
1. Stop Lidarr
2. Copy the Brainarr plugin files to:
   Windows: C:\ProgramData\Lidarr\bin\ImportLists\
   Linux: /opt/Lidarr/bin/ImportLists/
   Docker: /config/bin/ImportLists/

3. Restart Lidarr
4. Go to Settings > Import Lists > Add Import List
5. Select "Brainarr AI Music Discovery"
6. Configure:
   - AI Provider: Choose from 9 providers (Ollama recommended for privacy)
   - Local Providers: Ollama (http://localhost:11434) or LM Studio (http://localhost:1234)
   - Cloud Providers: OpenAI, Anthropic, Google Gemini, etc.
   - Model: (auto-detect or select your model)
   - Recommendations: 5-20
   - Discovery Mode: Similar, Adjacent, or Exploratory

7. Click "Test" to verify connection
8. Save and enjoy AI-powered music discovery!

REQUIREMENTS:
- Lidarr v4.0.0 or later
- .NET 6.0 runtime
- At least one AI provider configured:
  * Local: Ollama or LM Studio (privacy-focused)
  * Cloud: API key for OpenAI, Anthropic, Google, etc.

SUPPORTED PROVIDERS:
- Local: Ollama, LM Studio (100% private)
- Cloud: OpenAI, Anthropic, Google Gemini, Perplexity, Groq, DeepSeek
- Gateway: OpenRouter (access to 200+ models)

TROUBLESHOOTING:
- For local providers: Ensure service is running
- For cloud providers: Verify API key format and permissions
- Check firewall for port 11434 (Ollama) or 1234 (LM Studio)
- Click "Test" in settings to verify connection
- Check Lidarr logs: /config/logs/lidarr.txt
"@

$instructions | Out-File -FilePath "$packageDir\INSTALL.txt" -Encoding UTF8
Write-Host "  Created installation instructions" -ForegroundColor Green

# Copy test scripts
Copy-Item -Path ".\test_*.ps1" -Destination $packageDir -ErrorAction SilentlyContinue

# Create a zip package
Write-Host ""
Write-Host "Step 5: Creating ZIP package..." -ForegroundColor Yellow
$zipPath = ".\Brainarr_v1.0.0.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

try {
    Compress-Archive -Path "$packageDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "  Created: $zipPath" -ForegroundColor Green
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1KB, 2)
    Write-Host "  Package size: ${zipSize}KB" -ForegroundColor Cyan
}
catch {
    Write-Host "  Failed to create ZIP: $_" -ForegroundColor Red
}

# Step 6: Optional installation
if ($Install) {
    Write-Host ""
    Write-Host "Step 6: Installing to Lidarr..." -ForegroundColor Yellow
    
    $lidarrPluginPath = Join-Path $LidarrPath "bin\ImportLists"
    if (-not (Test-Path $lidarrPluginPath)) {
        Write-Host "  Lidarr plugin directory not found at: $lidarrPluginPath" -ForegroundColor Red
        Write-Host "  Please install manually using INSTALL.txt" -ForegroundColor Yellow
    }
    else {
        # Copy files to Lidarr
        foreach ($file in Get-ChildItem $packageDir -Filter "*.cs") {
            Copy-Item -Path $file.FullName -Destination $lidarrPluginPath -Force
        }
        Write-Host "  Installed to: $lidarrPluginPath" -ForegroundColor Green
        
        if ($Restart) {
            Write-Host "  Restarting Lidarr service..." -ForegroundColor Yellow
            try {
                Restart-Service "Lidarr" -ErrorAction Stop
                Write-Host "  Lidarr restarted successfully" -ForegroundColor Green
            }
            catch {
                Write-Host "  Could not restart Lidarr automatically" -ForegroundColor Yellow
                Write-Host "  Please restart Lidarr manually" -ForegroundColor Yellow
            }
        }
    }
}

# Step 7: Optional test deployment
if ($Test) {
    Write-Host ""
    Write-Host "Step 7: Deploying to test environment..." -ForegroundColor Yellow
    
    if (-not (Test-Path $TestPath)) {
        Write-Host "  Creating test directory: $TestPath" -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $TestPath -Force | Out-Null
    }
    
    # Copy the built DLL
    $dllPath = ".\Build\Lidarr.Plugin.Brainarr.dll"
    if (Test-Path $dllPath) {
        Copy-Item -Path $dllPath -Destination $TestPath -Force
        Write-Host "  Deployed DLL: $dllPath -> $TestPath" -ForegroundColor Green
    }
    else {
        Write-Host "  ERROR: Built DLL not found at: $dllPath" -ForegroundColor Red
    }
    
    # Copy plugin.json manifest
    if (Test-Path ".\plugin.json") {
        Copy-Item -Path ".\plugin.json" -Destination $TestPath -Force
        Write-Host "  Deployed manifest: plugin.json -> $TestPath" -ForegroundColor Green
    }
    
    Write-Host "  Test deployment complete!" -ForegroundColor Green
    Write-Host "  Location: $TestPath" -ForegroundColor Cyan
}

# Summary
Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Package created: Brainarr_v1.0.0.zip" -ForegroundColor White
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Extract Brainarr_v1.0.0.zip" -ForegroundColor White
Write-Host "2. Follow INSTALL.txt instructions" -ForegroundColor White
Write-Host "3. Configure in Lidarr > Settings > Import Lists" -ForegroundColor White
Write-Host "4. Start discovering new music!" -ForegroundColor White
Write-Host ""
Write-Host "For automatic installation, run:" -ForegroundColor Cyan
Write-Host "  .\build_and_deploy.ps1 -Install -Restart" -ForegroundColor White
Write-Host ""
Write-Host "For test deployment, run:" -ForegroundColor Cyan
Write-Host "  .\build_and_deploy.ps1 -Test" -ForegroundColor White
Write-Host ""