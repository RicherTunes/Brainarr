# 🚀 Brainarr Version Bump Script
# Usage: ./scripts/bump-version.ps1 -BumpType patch|minor|major

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("patch", "minor", "major")]
    [string]$BumpType,
    
    [switch]$DryRun
)

function Get-CurrentVersion {
    $pluginJson = Get-Content "plugin.json" | ConvertFrom-Json
    return $pluginJson.version
}

function Update-Version {
    param([string]$CurrentVersion, [string]$BumpType)
    
    $parts = $CurrentVersion.Split('.')
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]
    
    switch ($BumpType) {
        "major" { 
            $major++
            $minor = 0
            $patch = 0
        }
        "minor" { 
            $minor++
            $patch = 0
        }
        "patch" { 
            $patch++
        }
    }
    
    return "$major.$minor.$patch"
}

function Update-Files {
    param([string]$NewVersion)
    
    # Update plugin.json
    $pluginJson = Get-Content "plugin.json" | ConvertFrom-Json
    $pluginJson.version = $NewVersion
    $pluginJson | ConvertTo-Json -Depth 10 | Set-Content "plugin.json"
    
    # Update README.md version badge
    $readmeContent = Get-Content "README.md" -Raw
    $readmeContent = $readmeContent -replace "version-[\d\.]+", "version-$NewVersion"
    Set-Content "README.md" -Value $readmeContent
    
    Write-Host "✅ Updated plugin.json and README.md to version $NewVersion" -ForegroundColor Green
}

# Main script
try {
    $currentVersion = Get-CurrentVersion
    $newVersion = Update-Version -CurrentVersion $currentVersion -BumpType $BumpType
    
    Write-Host "🏷️ Version Bump: $BumpType" -ForegroundColor Cyan
    Write-Host "📋 Current Version: $currentVersion" -ForegroundColor Yellow
    Write-Host "🚀 New Version: $newVersion" -ForegroundColor Green
    
    if ($DryRun) {
        Write-Host "🔍 DRY RUN - No files will be modified" -ForegroundColor Magenta
        exit 0
    }
    
    # Confirm with user
    $confirm = Read-Host "Continue with version bump? (y/N)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Host "❌ Version bump cancelled" -ForegroundColor Red
        exit 1
    }
    
    # Update files
    Update-Files -NewVersion $newVersion
    
    Write-Host ""
    Write-Host "✅ Version bump complete!" -ForegroundColor Green
    Write-Host "📝 Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Review changes: git diff" -ForegroundColor White
    Write-Host "   2. Commit changes: git add . && git commit -m 'chore: bump version to $newVersion'" -ForegroundColor White
    Write-Host "   3. Create release: Go to GitHub Actions → Release Management → Run workflow" -ForegroundColor White
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}