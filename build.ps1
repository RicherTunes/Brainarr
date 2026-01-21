param(
    [switch]$Setup,
    [switch]$Test,
    [switch]$Package,
    [switch]$Clean,
    [switch]$Deploy,
    [switch]$Docs,
    [string]$Configuration = "Release",
    [string]$DeployPath = "X:\lidarr-hotio-test2\plugins\RicherTunes\Brainarr"
)

function Invoke-DocLint {
    Write-Host "`nRunning documentation lint..." -ForegroundColor Green

    $roots = @('docs', 'wiki-content') | Where-Object { Test-Path $_ }
    if ($roots.Count -eq 0) {
        Write-Host 'No documentation directories found (docs/, wiki-content/)' -ForegroundColor Yellow
        return
    }

    $markdown = $roots | ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter '*.md' }
    if ($markdown.Count -eq 0) {
        Write-Host 'No markdown files to lint' -ForegroundColor Yellow
        return
    }

    $violations = @()

    $doubleBracketHits = $markdown | Select-String -Pattern '\[\[' -SimpleMatch
    if ($doubleBracketHits) {
        Write-Host '::error ::Found wiki-style [[links]]; convert them to standard Markdown links.' -ForegroundColor Red
        $doubleBracketHits | Sort-Object Path, LineNumber | ForEach-Object {
            Write-Host ("  {0}:{1} -> {2}" -f $_.Path, $_.LineNumber, $_.Line.Trim()) -ForegroundColor Red
        }
        $violations += $doubleBracketHits
    }

    if ($violations.Count -gt 0) {
        Write-Host 'Documentation lint failed.' -ForegroundColor Red
        exit 1
    }

    Write-Host 'Documentation lint passed!' -ForegroundColor Green
}



function Invoke-PreTestCleanup {
    Write-Host "`n🧹 Pre-test cleanup: killing stale test hosts and cleaning artifacts..." -ForegroundColor Green
    Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process vstest.console -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $paths = @(".\Brainarr.Tests\bin", ".\Brainarr.Tests\obj")
    foreach ($p in $paths) {
        if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
    }
}$docOnly = $Docs -and -not ($Setup -or $Test -or $Package -or $Clean -or $Deploy)
if ($docOnly) {
    Invoke-DocLint
    exit $LASTEXITCODE
}


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
    ".\ext\Lidarr-docker\_output\net8.0",
    ".\ext\Lidarr\_output\net8.0",
    ".\ext\Lidarr\src\Lidarr\bin\Release\net8.0",
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
            try {
                Remove-Item $path -Recurse -Force
            }
            catch {
                Write-Host "Warning: Could not remove some files in $path (possibly locked by test host)" -ForegroundColor Yellow
                Write-Host "Attempting to kill any dotnet test processes..." -ForegroundColor Yellow
                Get-Process -Name "testhost*" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
                Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "*test*" } | Stop-Process -Force -ErrorAction SilentlyContinue

                # Wait a moment and try again
                Start-Sleep -Seconds 2
                try {
                    Remove-Item $path -Recurse -Force
                    Write-Host "Successfully removed $path after stopping test processes" -ForegroundColor Green
                }
                catch {
                    Write-Host "Warning: Still could not remove $path - continuing anyway" -ForegroundColor Yellow
                }
            }
        }
    }

    # Clean any existing packages (both old location and new artifacts/packages)
    Get-ChildItem -Path . -Name "Brainarr-*.zip" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Removing package: $_" -ForegroundColor Yellow
        Remove-Item $_
    }
    if (Test-Path ".\artifacts\packages") {
        Get-ChildItem -Path ".\artifacts\packages" -Filter "Brainarr-*.zip" -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "Removing package: $($_.FullName)" -ForegroundColor Yellow
            Remove-Item $_.FullName
        }
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
        try {`r`n            Invoke-PreTestCleanup`r`n            dotnet test -c $Configuration --no-build --blame-hang --blame-hang-timeout 60s
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
    Write-Host "`nPackaging plugin using unified PluginPack tooling..." -ForegroundColor Green
    $repoRoot = Get-Location

    # Load PluginPack module from Common submodule
    $modulePath = Join-Path $repoRoot 'ext/lidarr.plugin.common/tools/PluginPack.psm1'
    if (-not (Test-Path $modulePath)) {
        Write-Host "PluginPack.psm1 not found at $modulePath" -ForegroundColor Red
        Write-Host "Ensure Common submodule is initialized: git submodule update --init" -ForegroundColor Yellow
        exit 1
    }
    Import-Module $modulePath -Force

    # Find plugin project and manifest
    $pluginProject = Join-Path $repoRoot 'Brainarr.Plugin/Brainarr.Plugin.csproj'
    $manifestPath = Join-Path $repoRoot 'plugin.json'

    if (-not (Test-Path $pluginProject)) {
        Write-Host "Plugin project not found at: $pluginProject" -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path $manifestPath)) {
        Write-Host "plugin.json not found at repo root" -ForegroundColor Red
        exit 1
    }

    # Canonical Abstractions injection + entrypoint validation
    try {
        $packagePath = New-PluginPackage `
            -Csproj $pluginProject `
            -Manifest $manifestPath `
            -Framework 'net8.0' `
            -Configuration $Configuration `
            -RequireCanonicalAbstractions `
            -ResolveEntryPoints
        Write-Host "✅ Package created: $packagePath" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Packaging failed: $_" -ForegroundColor Red
        exit 1
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

if ($Docs) {
    Invoke-DocLint
}
