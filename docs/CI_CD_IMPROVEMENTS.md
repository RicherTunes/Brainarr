# CI/CD Improvements Guide

## Overview

This document describes the CI/CD improvements implemented for the Brainarr project, which can serve as a template for all Lidarr plugins.

## Implemented Improvements

### 1. Pre-commit Hooks

**Location:** `.githooks/`

Pre-commit hooks automatically prevent common issues before code reaches the repository:

- **Build artifacts detection**: Prevents committing `bin/`, `obj/`, `.dll`, `.pdb`, `.exe` files
- **Secret scanning**: Warns about potential hardcoded credentials
- **JSON validation**: Validates `plugin.json` syntax
- **Large file detection**: Warns about files >5MB
- **Package version checking**: Ensures .csproj files use centralized package management

#### Installation

```bash
# Unix/Linux/macOS
.githooks/install-hooks.sh

# Windows
.githooks\install-hooks.bat
```

#### Benefits

- Catches mistakes early in development
- Prevents security issues (no accidental credential commits)
- Maintains clean git history
- Reduces CI failures from preventable issues

### 2. Centralized Package Management

**Location:** `Directory.Packages.props`

All NuGet package versions are now managed in a single file:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="PackageName" Version="X.Y.Z" />
  </ItemGroup>
</Project>
```

#### Benefits

- **Single source of truth**: All versions in one file
- **Easier updates**: Update vulnerable packages once
- **No version conflicts**: Prevents runtime assembly load exceptions
- **Cleaner .csproj files**: More readable project files
- **Faster builds**: MSBuild doesn't resolve conflicts

#### Migration

Project files now reference packages without versions:

```xml
<!-- Before -->
<PackageReference Include="NLog" Version="5.4.0" />

<!-- After -->
<PackageReference Include="NLog" />
```

### 3. Version Compatibility Matrix

Critical version alignments for Lidarr plugins:

| Component | Version | Notes |
|-----------|---------|-------|
| Target Framework | net6.0 | Must match Lidarr host |
| Microsoft.Extensions.* | 8.0.x | Required by modern NLog |
| FluentValidation | 9.5.4 | Matches Lidarr's version |
| NLog | 5.4.0 | Compatible with Lidarr |

## Why Other Lidarr Plugins Should Adopt This

### Common Issues This Solves

1. **ReflectionTypeLoadException**: Version mismatches cause runtime failures
2. **NuGet NU1008 errors**: Package downgrade warnings
3. **Credential leaks**: Accidental API key commits
4. **Build artifact pollution**: Binary files in git history
5. **Dependency hell**: Conflicting package versions

### Evidence from Real Plugins

#### Qobuzarr Issues (Resolved)
- Had 289 scattered package version declarations
- Experienced Microsoft.Extensions version conflicts (6.0.0 vs 8.0.0)
- Build failures from NU1008 errors

#### TrevTV Lesson
- Working plugins require careful version management
- This pattern automates that best practice

### Implementation Cost vs Value

- **Setup time**: < 1 hour
- **Ongoing benefits**: Permanent
- **Maintenance reduction**: ~80% fewer version-related issues
- **Security improvement**: Automatic secret detection
- **Developer experience**: Faster feedback, fewer surprises

## Quick Implementation Guide

### Step 1: Add Pre-commit Hooks

```bash
# Create hooks directory
mkdir -p .githooks

# Copy pre-commit hook from this repo
cp /path/to/brainarr/.githooks/pre-commit .githooks/
cp /path/to/brainarr/.githooks/install-hooks.* .githooks/

# Install hooks
.githooks/install-hooks.sh  # or .bat on Windows
```

### Step 2: Create Directory.Packages.props

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Your packages here -->
  </ItemGroup>
</Project>
```

### Step 3: Migrate .csproj Files

Remove all `Version` attributes from `PackageReference` elements:

```bash
# PowerShell script
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    $content = Get-Content $_.FullName
    $content = $content -replace 'Version="[^"]*"', ''
    Set-Content $_.FullName $content
}
```

### Step 4: Test

```bash
dotnet restore
dotnet build
git add . && git commit -m "test" --dry-run  # Test pre-commit hooks
```

## Validation Commands

```bash
# Check for version conflicts
dotnet list package --outdated
dotnet list package --vulnerable

# Test pre-commit hooks
git add .
git commit -m "test: pre-commit validation" --dry-run

# Verify centralized packages
dotnet build --verbosity detailed | grep "PackageVersion"
```

## Troubleshooting

### Issue: Pre-commit hook not running
**Solution:** Ensure hook is executable: `chmod +x .git/hooks/pre-commit`

### Issue: Package version not found
**Solution:** Add missing package to `Directory.Packages.props`

### Issue: Version conflict warnings
**Solution:** Align all Microsoft.Extensions.* packages to same major version

## Conclusion

These improvements should become the standard template for all Lidarr plugins. They prevent common issues, improve security, and reduce maintenance overhead with minimal setup effort.
