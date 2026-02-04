# PowerShell script to analyze test coverage
param(
    [string]$TestResultsPath = "D:\Alex\github\brainarr\test-results"
)

# Get the latest TRX file
$trxFile = Get-ChildItem -Path $TestResultsPath -Filter "*.trx" | Sort-Object LastWriteTime | Select-Object -Last 1

if (-not $trxFile) {
    Write-Host "No TRX file found in $TestResultsPath"
    exit 1
}

Write-Host "Analyzing TRX file: $($trxFile.FullName)"

# Parse the TRX file
[xml]$trxContent = Get-Content $trxFile.FullName

# Get test results
$testResults = $trxContent.SelectNodes("//UnitTestResult")
$totalTests = $testResults.Count
$passedTests = ($testResults | Where-Object { $_.outcome -eq "Passed" }).Count
$failedTests = ($testResults | Where-Object { $_.outcome -eq "Failed" }).Count
$skippedTests = ($testResults | Where-Object { $_.outcome -eq "NotExecuted" }).Count

Write-Host ""
Write-Host "=== Test Results Summary ==="
Write-Host "Total tests: $totalTests"
Write-Host "Passed: $passedTests"
Write-Host "Failed: $failedTests"
Write-Host "Skipped: $skippedTests"
Write-Host "Pass rate: $([math]::Round(($passedTests / $totalTests) * 100, 1))%"
Write-Host ""

# Find coverage files
$coverageFiles = Get-ChildItem -Path $TestResultsPath -Filter "coverage.cobertura.xml" -Recurse

if ($coverageFiles.Count -eq 0) {
    Write-Host "No coverage files found. Cannot calculate coverage metrics."
    exit 1
}

# Analyze each coverage file
$overallLineRate = 0
$overallBranchRate = 0
$totalLinesCovered = 0
$totalLinesValid = 0
$totalBranchesCovered = 0
$totalBranchesValid = 0

foreach ($file in $coverageFiles) {
    [xml]$coverageContent = Get-Content $file.FullName

    $lineRate = [double]$coverageContent.coverage.'line-rate'
    $branchRate = [double]$coverageContent.coverage.'branch-rate'
    $linesCovered = [int]$coverageContent.coverage.'lines-covered'
    $linesValid = [int]$coverageContent.coverage.'lines-valid'
    $branchesCovered = [int]$coverageContent.coverage.'branches-covered'
    $branchesValid = [int]$coverageContent.coverage.'branches-valid'

    $overallLineRate += $lineRate
    $overallBranchRate += $branchRate
    $totalLinesCovered += $linesCovered
    $totalLinesValid += $linesValid
    $totalBranchesCovered += $branchesCovered
    $totalBranchesValid += $branchesValid

    Write-Host "Coverage file: $($file.Name)"
    Write-Host "  Line rate: $([math]::Round($lineRate * 100, 1))%"
    Write-Host "  Branch rate: $([math]::Round($branchRate * 100, 1))%"
    Write-Host "  Lines covered/valid: $linesCovered/$linesValid"
    Write-Host "  Branches covered/valid: $branchesCovered/$branchesValid"
    Write-Host ""
}

# Calculate averages
$averageLineRate = $overallLineRate / $coverageFiles.Count
$averageBranchRate = $overallBranchRate / $coverageFiles.Count

Write-Host "=== Overall Coverage Summary ==="
Write-Host "Average line coverage: $([math]::Round($averageLineRate * 100, 1))%"
Write-Host "Average branch coverage: $([math]::Round($averageBranchRate * 100, 1))%"
Write-Host "Total lines covered/valid: $totalLinesCovered/$totalLinesValid"
Write-Host "Total branches covered/valid: $totalBranchesCovered/$totalBranchesValid"

# Find least covered files
Write-Host ""
Write-Host "=== Analysis of Coverage Collection Issues ==="
Write-Host "Note: Coverage collection appears to be generating empty files."
Write-Host "This could be due to:"
Write-Host "1. Test failures preventing coverage collection"
Write-Host "2. Permission issues with coverage generation"
Write-Host "3. Configuration issues with the test runner"
Write-Host ""
Write-Host "As a baseline, here's the test status:"
Write-Host "- $passedTests/$totalTests tests passed ($([math]::Round(($passedTests / $totalTests) * 100, 1))% pass rate)"
Write-Host "- $failedTests tests failed"
Write-Host "- $skippedTests tests skipped"