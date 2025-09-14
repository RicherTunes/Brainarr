param(
    [string]$ResultsRoot = "TestResults",
    [string]$ReportDir = "TestResults/CoverageReport",
    [switch]$InstallTool
)

Write-Host "📊 Generating HTML coverage report..." -ForegroundColor Yellow

if ($InstallTool) {
    try {
        dotnet tool install -g dotnet-reportgenerator-globaltool | Out-Null
        # Ensure PATH includes global tool locations for both Windows and *nix
        $toolPaths = @()
        $toolPaths += Join-Path $env:USERPROFILE ".dotnet\tools"           # Linux/mac style on Windows sometimes
        $toolPaths += Join-Path $env:USERPROFILE "AppData\Local\Microsoft\dotnet\tools"  # Windows default
        $toolPaths += (Join-Path $env:HOME ".dotnet/tools")                 # *nix default
        foreach ($p in $toolPaths) {
            if ($p -and (Test-Path $p) -and ($env:PATH -notlike "*${p}*")) {
                $env:PATH = "$p;$env:PATH"
            }
        }
        Write-Host "✅ Installed reportgenerator" -ForegroundColor Green
    } catch {
        Write-Warning "Failed to install reportgenerator: $($_.Exception.Message)"
    }
}

$reports = Get-ChildItem $ResultsRoot -Recurse -Filter coverage.cobertura.xml | Sort-Object LastWriteTime -Descending
if (-not $reports) {
    Write-Warning "No coverage.cobertura.xml files found under '$ResultsRoot'"
    exit 1
}

$latest = $reports[0].FullName
Write-Host "Using coverage file: $latest" -ForegroundColor Cyan

if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    Write-Warning "reportgenerator not found on PATH. Install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
    exit 2
}

if (-not (Test-Path $ReportDir)) { New-Item -ItemType Directory -Path $ReportDir | Out-Null }

reportgenerator "-reports:$latest" "-targetdir:$ReportDir" "-reporttypes:HtmlInline_AzurePipelines;TextSummary" | Out-Null

Write-Host "📑 Coverage report: $ReportDir/index.html" -ForegroundColor Green
Write-Host "📝 Summary:" -ForegroundColor Yellow
Get-Content -Path (Join-Path $ReportDir "Summary.txt") -ErrorAction SilentlyContinue | Select-Object -First 30 | ForEach-Object { Write-Host $_ }

exit 0
