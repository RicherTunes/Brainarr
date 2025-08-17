param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('major', 'minor', 'patch', 'build')]
    [string]$BumpType = 'build',
    
    [Parameter(Mandatory=$false)]
    [string]$Version = '',
    
    [Parameter(Mandatory=$false)]
    [string]$Suffix = ''
)

$ErrorActionPreference = 'Stop'

# Read current version
$versionFile = Join-Path $PSScriptRoot '..\version.json'
$versionData = Get-Content $versionFile | ConvertFrom-Json

# Parse current version
$currentVersion = [Version]$versionData.version
$currentBuild = $versionData.buildNumber

Write-Host "Current version: $($currentVersion).$currentBuild" -ForegroundColor Cyan

# Determine new version
if ($Version) {
    # Explicit version provided
    $newVersion = [Version]$Version
    $newBuild = 0
} else {
    # Bump version based on type
    switch ($BumpType) {
        'major' {
            $newVersion = [Version]::new($currentVersion.Major + 1, 0, 0)
            $newBuild = 0
        }
        'minor' {
            $newVersion = [Version]::new($currentVersion.Major, $currentVersion.Minor + 1, 0)
            $newBuild = 0
        }
        'patch' {
            $newVersion = [Version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1)
            $newBuild = 0
        }
        'build' {
            $newVersion = $currentVersion
            $newBuild = $currentBuild + 1
        }
    }
}

# Create full version string
$fullVersion = "$($newVersion.Major).$($newVersion.Minor).$($newVersion.Build).$newBuild"
$semVersion = "$($newVersion.Major).$($newVersion.Minor).$($newVersion.Build)"
if ($Suffix) {
    $semVersion = "$semVersion-$Suffix"
}

Write-Host "New version: $fullVersion" -ForegroundColor Green

# Update version.json
$versionData.version = "$($newVersion.Major).$($newVersion.Minor).$($newVersion.Build)"
$versionData.buildNumber = $newBuild
$versionData.suffix = $Suffix
$versionData | ConvertTo-Json | Set-Content $versionFile

# Update Directory.Build.props
$propsFile = Join-Path $PSScriptRoot '..\Directory.Build.props'
$propsContent = Get-Content $propsFile -Raw
$propsContent = $propsContent -replace '<Version>.*</Version>', "<Version>$semVersion</Version>"
$propsContent = $propsContent -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$fullVersion</AssemblyVersion>"
$propsContent = $propsContent -replace '<FileVersion>.*</FileVersion>', "<FileVersion>$fullVersion</FileVersion>"
$propsContent = $propsContent -replace '<InformationalVersion>.*</InformationalVersion>', "<InformationalVersion>$semVersion</InformationalVersion>"
$propsContent | Set-Content $propsFile -NoNewline

# Update plugin.json if it exists
$pluginFile = Join-Path $PSScriptRoot '..\plugin.json'
if (Test-Path $pluginFile) {
    $pluginData = Get-Content $pluginFile | ConvertFrom-Json
    $pluginData.version = $semVersion
    $pluginData | ConvertTo-Json -Depth 10 | Set-Content $pluginFile
    Write-Host "Updated plugin.json" -ForegroundColor Yellow
}

Write-Host "Version updated successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Commit the version changes"
Write-Host "2. Create a git tag: git tag v$semVersion"
Write-Host "3. Push the tag: git push origin v$semVersion"

# Output for GitHub Actions
if ($env:GITHUB_OUTPUT) {
    "version=$semVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "full_version=$fullVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}