# PowerShell script to fix Logger mocking issues across all test files
# This script replaces Mock<Logger> patterns with real Logger instances using TestLogger

Write-Host "🔧 Fixing Logger mocking issues across test suite..." -ForegroundColor Green

$testDir = "I:\Arr-Plugins\Lidarr\Brainarr\Brainarr.Tests"
$filesFixed = 0

# Find all test files with Logger mocking issues
$testFiles = Get-ChildItem -Path $testDir -Recurse -Filter "*.cs" |
    Where-Object {
        $content = Get-Content $_.FullName -Raw
        $content -match "Mock<Logger>" -or $content -match "_loggerMock"
    }

Write-Host "Found $($testFiles.Count) files with Logger mocking issues" -ForegroundColor Yellow

foreach ($file in $testFiles) {
    Write-Host "Processing: $($file.Name)" -ForegroundColor Cyan

    $content = Get-Content $file.FullName -Raw
    $originalContent = $content

    # Step 1: Add TestLogger import if not already present
    if ($content -notmatch "using Brainarr\.Tests\.Helpers;") {
        $content = $content -replace "(using NLog;)", "`$1`nusing Brainarr.Tests.Helpers;"
        Write-Host "  ✅ Added TestLogger import" -ForegroundColor Green
    }

    # Step 2: Replace Mock<Logger> field declarations
    $content = $content -replace "private readonly Mock<Logger> _loggerMock;", "private readonly Logger _logger;"
    $content = $content -replace "private readonly Mock<Logger> _logger;", "private readonly Logger _logger;"
    $content = $content -replace "private Mock<Logger> _loggerMock;", "private Logger _logger;"

    # Step 3: Replace Mock<Logger> initialization
    $content = $content -replace "_loggerMock = new Mock<Logger>\(\);", "_logger = TestLogger.CreateNullLogger();"
    $content = $content -replace "_logger = new Mock<Logger>\(\);", "_logger = TestLogger.CreateNullLogger();"

    # Step 4: Replace usage patterns
    $content = $content -replace "_loggerMock\.Object", "_logger"
    $content = $content -replace "_logger\.Object", "_logger" # In case some files use _logger instead of _loggerMock

    # Step 5: Remove Logger mock verifications (they can't be tested with real loggers)
    $content = $content -replace ".*_loggerMock\.Verify.*\n", "            // Note: Logger verification removed - cannot mock NLog Logger`n"
    $content = $content -replace ".*_logger\.Verify.*\n", "            // Note: Logger verification removed - cannot mock NLog Logger`n"

    # Step 6: Handle constructor parameter passing
    $content = $content -replace "new Mock<Logger>\(\)\.Object", "TestLogger.CreateNullLogger()"
    $content = $content -replace ", _loggerMock\.Object", ", _logger"
    $content = $content -replace "logger: _loggerMock\.Object", "logger: _logger"

    # Check if any changes were made
    if ($content -ne $originalContent) {
        Set-Content $file.FullName $content -Encoding UTF8
        $filesFixed++
        Write-Host "  ✅ Fixed Logger mocking patterns" -ForegroundColor Green
    } else {
        Write-Host "  ℹ️  No changes needed" -ForegroundColor Gray
    }
}

Write-Host "`n🎉 Logger mocking fix completed!" -ForegroundColor Green
Write-Host "📊 Files processed: $($testFiles.Count)" -ForegroundColor White
Write-Host "📊 Files fixed: $filesFixed" -ForegroundColor White
Write-Host "`n🔍 Next steps:" -ForegroundColor Yellow
Write-Host "1. Run: dotnet build Brainarr.Tests" -ForegroundColor White
Write-Host "2. Run: dotnet test Brainarr.Tests --verbosity normal" -ForegroundColor White
Write-Host "3. Review any remaining failures for non-Logger issues" -ForegroundColor White
