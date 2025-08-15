# Production Build Strategy for Brainarr Plugin

## Overview

This document describes the production-ready build strategy designed to solve the Lidarr plugin version compatibility challenges. The solution addresses the core issue where the Lidarr plugins branch uses `AssemblyVersion 10.0.0.*` but runtime Lidarr uses versions like `2.13.2.4686`.

## Problem Analysis

### Root Cause
- **Plugin Branch Mismatch**: Lidarr plugins branch has AssemblyVersion `10.0.0.*`
- **Runtime Reality**: Production Lidarr runtime uses `2.13.x.xxxx` versions  
- **Loading Failure**: .NET assembly loading requires exact version matches
- **Manual Hacks**: Previous solution required manual version editing (not CI/CD compatible)
- **Dependency Conflicts**: Lidarr plugin system has bugs requiring assembly merging

### Research Findings
- **TrevTV Pattern**: Uses build-time version replacement via GitHub Actions
- **ILRepack Necessity**: All successful plugins bundle dependencies due to Lidarr bugs
- **Runtime Targeting**: Plugins must target specific runtime versions like `2.13.1.4681`
- **CI Integration**: Build-time overrides allow maintainable CI/CD

## Solution Architecture

### Multi-Strategy Build System

Our solution implements a comprehensive build system with four key components:

#### 1. Build-Time Version Override (`Directory.Build.props`)
```xml
<!-- Runtime compatibility targeting -->
<LidarrTargetVersion Condition="'$(LidarrTargetVersion)' == ''">2.13.1.4681</LidarrTargetVersion>
<AssemblyVersion>$(LidarrTargetVersion)</AssemblyVersion>
```

**Benefits:**
- ✅ Version set at build time, not in source
- ✅ CI/CD compatible with parameter override
- ✅ Maintains source code compatibility
- ✅ Multiple target versions supported

#### 2. Dependency Bundling (`ILRepack.targets`)
```xml
<!-- Bundle plugin dependencies to avoid Lidarr plugin system bugs -->
<ILRepackLibraries>Newtonsoft.Json.dll;NLog.dll;FluentValidation.dll</ILRepackLibraries>
```

**Benefits:**
- ✅ Solves Lidarr plugin system dependency loading bugs
- ✅ Single DLL deployment (easier distribution)
- ✅ Eliminates version conflicts with Lidarr dependencies
- ✅ Follows successful plugin patterns (TrevTV approach)

#### 3. Smart Build Script (`build-production.ps1`)
```powershell
# Intelligent build with environment detection
-LidarrTargetVersion "2.13.1.4681" -PluginVersion "1.0.0" -CI -Package
```

**Features:**
- 🔧 Automatic environment detection (Local vs CI)
- 🔧 Version compatibility validation  
- 🔧 ILRepack integration with fallback
- 🔧 Comprehensive error handling
- 🔧 Packaging for distribution

#### 4. Automated CI/CD Pipeline (`.github/workflows/build-and-release.yml`)
```yaml
# Multi-target compatibility testing
strategy:
  matrix:
    lidarr-version: ['2.13.1.4681', '2.13.2.4686']
```

**Capabilities:**
- 🚀 Automated builds on push/PR
- 🚀 Multi-version compatibility testing
- 🚀 Automatic releases on tags
- 🚀 Security scanning
- 🚀 Artifact management

## Usage Guide

### Quick Start

1. **Auto-detect your Lidarr version:**
```powershell
.\detect-lidarr-version.ps1 -AutoDetect
```

2. **Build for your version:**
```powershell
.\build-production.ps1 -LidarrTargetVersion "2.13.1.4681" -Package
```

3. **Deploy the package:**
   - Extract `Brainarr-v1.0.0.zip`
   - Copy files to Lidarr plugins directory
   - Restart Lidarr

### Development Workflow

#### Local Development
```powershell
# Standard development build
.\build-production.ps1

# Clean build with specific version
.\build-production.ps1 -Clean -LidarrTargetVersion "2.13.2.4686"

# Debug build without ILRepack
.\build-production.ps1 -Configuration Debug -NoILRepack
```

#### CI/CD Integration
```bash
# Environment variable approach
export LIDARR_TARGET_VERSION="2.13.1.4681"
pwsh -File ./build-production.ps1 -CI -Package

# Parameter approach  
pwsh -File ./build-production.ps1 -LidarrTargetVersion "2.13.1.4681" -CI
```

### Version Targeting Strategies

#### Strategy 1: Single Target (Recommended)
Target the most common stable version:
```powershell
-LidarrTargetVersion "2.13.1.4681"
```
**Pros:** Maximum compatibility, single maintenance burden
**Cons:** May not work with bleeding-edge Lidarr

#### Strategy 2: Multi-Target Build
Build separate packages for different versions:
```powershell
# Stable users
.\build-production.ps1 -LidarrTargetVersion "2.13.1.4681" -PluginVersion "1.0.0-stable"

# Latest users  
.\build-production.ps1 -LidarrTargetVersion "2.13.2.4686" -PluginVersion "1.0.0-latest"
```
**Pros:** Maximum coverage
**Cons:** Multiple packages to maintain

#### Strategy 3: Auto-Detection
Let the build system detect locally installed Lidarr:
```powershell
# Auto-detect and use local Lidarr version
.\detect-lidarr-version.ps1 -AutoDetect
# Use recommended version from detection
.\build-production.ps1 -LidarrTargetVersion "<detected-version>"
```

## Advanced Configuration

### Environment Variables
```bash
# CI/CD environment variables
export LIDARR_TARGET_VERSION="2.13.1.4681"
export LIDARR_OVERRIDE_VERSION="2.13.2.4686"  # Override in CI
export GITHUB_ACTIONS="true"
export GITHUB_RUN_NUMBER="123"
```

### MSBuild Properties
```powershell
# Advanced MSBuild property overrides
dotnet build -p:LidarrTargetVersion="2.13.1.4681" -p:ILRepackEnabled=false
```

### Docker Builds
```dockerfile
# Dockerfile integration
RUN pwsh -File ./build-production.ps1 -CI -LidarrTargetVersion "2.13.1.4681"
```

## Troubleshooting

### Common Issues

#### Issue: "Assembly version mismatch"
**Cause:** Plugin AssemblyVersion doesn't match Lidarr runtime
**Solution:** 
```powershell
# Detect correct version
.\detect-lidarr-version.ps1 -AutoDetect
# Build with detected version
.\build-production.ps1 -LidarrTargetVersion "<detected-version>"
```

#### Issue: "Could not load dependency X"
**Cause:** Lidarr plugin system dependency loading bug
**Solution:** Ensure ILRepack is enabled (default)
```powershell
# Force ILRepack 
.\build-production.ps1 -LidarrTargetVersion "2.13.1.4681"
# Verify merged DLL
ls Build/Lidarr.Plugin.Brainarr.dll  # Should be larger than original
```

#### Issue: "Plugin not loading in Lidarr"
**Solutions:**
1. **Check Lidarr logs** for specific error messages
2. **Verify plugin directory** (`/config/Plugins/` or `C:\ProgramData\Lidarr\Plugins\`)  
3. **Confirm plugin.json** is in same directory as DLL
4. **Restart Lidarr** after plugin installation

#### Issue: "Build fails on CI"
**Common Causes:**
- Missing environment variables
- PowerShell execution policy
- .NET SDK version mismatch

**Solutions:**
```yaml
# GitHub Actions fix
- name: Setup PowerShell
  uses: microsoft/setup-powershell@v1
- name: Set execution policy  
  run: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

## Best Practices

### 1. Version Management
- ✅ Always use build-time version targeting
- ✅ Never hardcode AssemblyVersion in source
- ✅ Use semantic versioning for plugin releases
- ✅ Test against multiple Lidarr versions in CI

### 2. Dependency Management  
- ✅ Always use ILRepack for production builds
- ✅ Exclude Lidarr assemblies from bundling
- ✅ Pin dependency versions for reproducible builds
- ✅ Regular dependency updates with compatibility testing

### 3. Build Hygiene
- ✅ Clean builds for releases
- ✅ Validate plugin loading after build  
- ✅ Package with installation instructions
- ✅ Comprehensive CI/CD testing

### 4. Distribution
- ✅ Provide multiple download options (GitHub releases, direct download)
- ✅ Include version compatibility information
- ✅ Clear installation instructions
- ✅ Support documentation links

## Migration Guide

### From Manual Version Editing

**Old approach:**
1. Edit `AssemblyInfo.cs` manually
2. Change `AssemblyVersion("10.0.0.0")` to `AssemblyVersion("2.13.1.4681")`
3. Build and deploy

**New approach:**
1. No source code changes needed
2. Use: `.\build-production.ps1 -LidarrTargetVersion "2.13.1.4681"`
3. Deploy generated package

### From Basic MSBuild

**Old approach:**
```powershell
dotnet build -c Release
```

**New approach:**
```powershell
.\build-production.ps1 -Configuration Release -Package
```

## Technical Implementation Details

### Directory.Build.props Features
- **Environment Detection**: Automatic CI vs Local detection
- **Version Calculation**: Smart version numbering with build numbers
- **Assembly Overrides**: Controlled AssemblyVersion targeting
- **MSBuild Integration**: Seamless integration with existing workflows

### ILRepack.targets Features
- **Selective Bundling**: Only bundle plugin-specific dependencies
- **Fallback Support**: Multiple ILRepack execution strategies
- **Validation**: Post-merge validation and reporting
- **Error Recovery**: Graceful handling of merge failures

### Build Script Features
- **Cross-Platform**: PowerShell Core compatible (Windows/Linux/macOS)
- **Validation**: Comprehensive prerequisite and result validation
- **Packaging**: Automated ZIP creation with instructions
- **CI Integration**: GitHub Actions and Azure DevOps compatible

## Future Enhancements

### Planned Features
- [ ] **Multi-Runtime Support**: Build for .NET 6/7/8 in single workflow
- [ ] **Plugin Signing**: Code signing for enhanced security
- [ ] **Auto-Update**: Plugin auto-update mechanism
- [ ] **Version Matrix**: Automated compatibility matrix generation

### Community Contributions
- [ ] **Plugin Templates**: Reusable templates for other Lidarr plugins
- [ ] **Build Tools**: Standalone build tools for plugin developers  
- [ ] **Documentation**: Community-contributed guides and tutorials

## Conclusion

This production-ready build strategy solves the core Lidarr plugin version compatibility challenge through:

1. **Build-time version targeting** eliminates manual source editing
2. **ILRepack dependency bundling** works around Lidarr plugin system bugs  
3. **Intelligent build automation** supports both local development and CI/CD
4. **Multi-version compatibility testing** ensures broad Lidarr version support
5. **Comprehensive error handling** provides clear troubleshooting guidance

The system is maintainable, CI/CD friendly, and follows patterns established by successful plugins in the ecosystem.

**Result**: A foolproof, production-ready build system that works with runtime Lidarr versions without manual intervention.