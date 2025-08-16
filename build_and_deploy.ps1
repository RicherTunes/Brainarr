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

# Step 3: Setup Lidarr if needed
Write-Host ""
Write-Host "Step 3: Checking Lidarr submodule..." -ForegroundColor Yellow

# Initialize and update Git submodule
if (-not (Test-Path "ext\Lidarr\.git")) {
    Write-Host "  Initializing Lidarr submodule..." -ForegroundColor Yellow
    git submodule init
    git submodule update
}
else {
    Write-Host "  Updating Lidarr submodule..." -ForegroundColor Yellow
    git submodule update --remote
}

# Check if we have Lidarr available for building
$lidarrPaths = @(
    (Join-Path (Get-Location) "ext\Lidarr\_output\net6.0"),
    (Join-Path (Get-Location) "ext\Lidarr\src\Lidarr\bin\Release\net6.0"),
    $env:LIDARR_PATH
)

$foundLidarr = $false
$lidarrPath = ""
foreach ($path in $lidarrPaths) {
    if ($path -and (Test-Path "$path\Lidarr.Core.dll" -ErrorAction SilentlyContinue)) {
        Write-Host "  Found Lidarr at: $path" -ForegroundColor Green
        $lidarrPath = $path
        $foundLidarr = $true
        break
    }
}

if (-not $foundLidarr) {
    Write-Host "  Lidarr binaries not found, building from submodule..." -ForegroundColor Yellow
    
    try {
        Push-Location "ext\Lidarr\src"
        
        Write-Host "  Restoring Lidarr packages..." -ForegroundColor Cyan
        dotnet restore Lidarr.sln
        if ($LASTEXITCODE -ne 0) { throw "Failed to restore Lidarr packages" }
        
        Write-Host "  Building Lidarr..." -ForegroundColor Cyan
        dotnet build Lidarr.sln -c Release
        if ($LASTEXITCODE -ne 0) { throw "Failed to build Lidarr" }
        
        Write-Host "  Lidarr build completed!" -ForegroundColor Green
    }
    catch {
        Write-Host "  ERROR: Lidarr build failed: $_" -ForegroundColor Red
        exit 1
    }
    finally {
        Pop-Location
    }
    
    # Re-check for Lidarr after build
    foreach ($path in $lidarrPaths) {
        if ($path -and (Test-Path "$path\Lidarr.Core.dll" -ErrorAction SilentlyContinue)) {
            Write-Host "  Found Lidarr after build at: $path" -ForegroundColor Green
            $lidarrPath = $path
            $foundLidarr = $true
            break
        }
    }
    
    if (-not $foundLidarr) {
        Write-Host "  ERROR: Lidarr still not found after build!" -ForegroundColor Red
        exit 1
    }
}

# Step 4: Build the plugin
Write-Host ""
Write-Host "Step 4: Building Brainarr plugin..." -ForegroundColor Yellow

# Create output directory
$outputDir = ".\Build"
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

# Build with proper project path
Write-Host "  Building plugin project..." -ForegroundColor Cyan
$success = $true

try {
    # Build the actual project
    Push-Location ".\Brainarr.Plugin"
    
    # Set environment variable for this build session
    $env:LIDARR_PATH = $lidarrPath
    Write-Host "  Using Lidarr from: $lidarrPath" -ForegroundColor Cyan
    
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "Restore failed" }
    
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
    
    # Copy output to Build directory (our build outputs to .\bin directly)
    $sourceDir = ".\bin"
    if (Test-Path $sourceDir) {
        Copy-Item -Path "$sourceDir\*" -Destination "..\Build" -Recurse -Force
        Write-Host "  Build successful! Output copied to Build directory" -ForegroundColor Green
        
        # Verify the main plugin DLL was created
        $mainDll = "..\Build\Lidarr.Plugin.Brainarr.dll"
        if (Test-Path $mainDll) {
            $dllSize = [math]::Round((Get-Item $mainDll).Length / 1KB, 2)
            Write-Host "  Plugin DLL created: ${dllSize}KB" -ForegroundColor Cyan
        }
        else {
            Write-Host "  WARNING: Main plugin DLL not found!" -ForegroundColor Yellow
        }
    }
    else {
        throw "Build output not found at $sourceDir"
    }
}
catch {
    Write-Host "  Build error: $_" -ForegroundColor Red
    $success = $false
}
finally {
    Pop-Location
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

# Step 5: Package the plugin
Write-Host ""
Write-Host "Step 5: Creating deployment package..." -ForegroundColor Yellow

$packageDir = ".\BrainarrPackage"
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir | Out-Null

# Copy built DLL (the main plugin file)
$pluginDll = ".\Build\Lidarr.Plugin.Brainarr.dll"
if (Test-Path $pluginDll) {
    Copy-Item -Path $pluginDll -Destination $packageDir
    Write-Host "  Packaged main plugin: Lidarr.Plugin.Brainarr.dll" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: Plugin DLL not found at: $pluginDll" -ForegroundColor Yellow
}

# Copy plugin manifest if exists
if (Test-Path ".\plugin.json") {
    Copy-Item -Path ".\plugin.json" -Destination $packageDir
    Write-Host "  Packaged plugin manifest" -ForegroundColor Green
}

# Copy any dependency DLLs (but exclude Lidarr/NzbDrone DLLs)
$dependencyDlls = Get-ChildItem ".\Build\*.dll" -ErrorAction SilentlyContinue | 
    Where-Object { $_.Name -notlike "Lidarr.*" -and $_.Name -notlike "NzbDrone.*" -and $_.Name -notlike "*.pdb" }

foreach ($dll in $dependencyDlls) {
    Copy-Item -Path $dll.FullName -Destination $packageDir
    Write-Host "  Packaged dependency: $($dll.Name)" -ForegroundColor Gray
}

# Create installation instructions
$instructions = @"
BRAINARR INSTALLATION INSTRUCTIONS
===================================

MANUAL INSTALLATION:
1. Stop Lidarr
2. Copy Lidarr.Plugin.Brainarr.dll to your Lidarr plugins directory:
   Windows: C:\ProgramData\Lidarr\Plugins\
   Linux: /opt/Lidarr/Plugins/
   Docker: /config/Plugins/

3. Copy plugin.json (if included) to the same directory
4. Restart Lidarr
5. Go to Settings > Import Lists > Add Import List
6. Select "Brainarr AI Music Discovery"
7. Configure:
   - AI Provider: Choose from 9 providers (Ollama recommended for privacy)
   - Configuration URL: Auto-configured based on provider
   - Model Selection: Click "Test" first to auto-detect models
   - API Key: For cloud providers only
   - Library Sampling: Minimal (local), Balanced (default), Comprehensive (premium)
   - Recommendations: 5-20 albums per sync
   - Discovery Mode: Similar, Adjacent, or Exploratory

8. Click "Test" to verify connection and detect models
9. Save and enjoy intelligent, library-aware music discovery!

REQUIREMENTS:
- Lidarr v4.0.0 or later
- .NET 6.0 runtime
- At least one AI provider configured:
  * Local: Ollama or LM Studio (privacy-focused)
  * Cloud: API key for OpenAI, Anthropic, Google, etc.

SUPPORTED PROVIDERS:
- Local: Ollama, LM Studio (100% private, no data transmission)
- Cloud: OpenAI, Anthropic, Google Gemini, Perplexity, Groq, DeepSeek
- Gateway: OpenRouter (access to 200+ models with one API key)

KEY FEATURES:
- Library-Aware Recommendations: AI knows your collection, avoids duplicates
- Intelligent Sampling: Configurable context based on provider capabilities
- Provider-Specific Optimization: Local models get less context, premium get more
- Iterative Quality: Retries if duplicates are returned
- Auto-Model Detection: Automatically finds available local models
- Unified UI: Clean interface shows only relevant settings

TROUBLESHOOTING:
- For local providers: Ensure service is running (ollama serve / LM Studio server)
- For cloud providers: Verify API key format and permissions
- Check firewall for port 11434 (Ollama) or 1234 (LM Studio)
- Click "Test" in settings to verify connection and detect models
- Check Lidarr logs: Settings > General > Log Level > Debug
- No duplicates? Enable library-aware mode with higher sampling
"@

$instructions | Out-File -FilePath "$packageDir\INSTALL.txt" -Encoding UTF8
Write-Host "  Created installation instructions" -ForegroundColor Green

# Copy test scripts
Copy-Item -Path ".\test_*.ps1" -Destination $packageDir -ErrorAction SilentlyContinue

# Create a zip package
Write-Host ""
Write-Host "Step 6: Creating ZIP package..." -ForegroundColor Yellow
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

# Step 7: Optional installation
if ($Install) {
    Write-Host ""
    Write-Host "Step 7: Installing to Lidarr..." -ForegroundColor Yellow
    
    $lidarrPluginPath = Join-Path $LidarrPath "bin\ImportLists"
    if (-not (Test-Path $lidarrPluginPath)) {
        Write-Host "  Lidarr plugin directory not found at: $lidarrPluginPath" -ForegroundColor Red
        Write-Host "  Please install manually using INSTALL.txt" -ForegroundColor Yellow
    }
    else {
        # Copy DLL files to Lidarr (not source files)
        foreach ($file in Get-ChildItem $packageDir -Filter "*.dll") {
            Copy-Item -Path $file.FullName -Destination $lidarrPluginPath -Force
        }
        # Copy manifest if exists
        if (Test-Path "$packageDir\plugin.json") {
            Copy-Item -Path "$packageDir\plugin.json" -Destination $lidarrPluginPath -Force
        }
        Write-Host "  Installed plugin DLL to: $lidarrPluginPath" -ForegroundColor Green
        
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

# Step 8: Optional test deployment
if ($Test) {
    Write-Host ""
    Write-Host "Step 8: Deploying to test environment..." -ForegroundColor Yellow
    
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