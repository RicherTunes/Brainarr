# Lidarr CI Stubs

This directory contains minimal stub assemblies that provide the necessary Lidarr type definitions for CI builds without requiring the full Lidarr source code.

## Purpose

These stubs solve the CI build problem where the Brainarr plugin references Lidarr namespaces that don't exist in mock assemblies. Instead of using empty XML files as "mock" DLLs, these projects provide actual .NET assemblies with the required type definitions.

## Structure

- **Lidarr.Core.Stubs.csproj** - Contains core Lidarr types and interfaces
- **Lidarr.Common.Stubs.csproj** - Contains common HTTP and utility types
- **LidarrStubs.sln** - Solution file organizing both projects

## Key Namespaces Provided

### NzbDrone.Core.*
- `NzbDrone.Core.Annotations` - UI field definitions and attributes
- `NzbDrone.Core.Validation` - Validation types and enums
- `NzbDrone.Core.ImportLists` - Base import list classes and interfaces
- `NzbDrone.Core.Parser.Model` - Import list item models
- `NzbDrone.Core.Music` - Music entities (Artist, Album, Track)
- `NzbDrone.Core.MetadataSource` - Metadata service interfaces
- `NzbDrone.Core.Configuration` - Configuration service interfaces
- `NzbDrone.Core.Parser` - Parsing service interfaces
- `NzbDrone.Core.Datastore` - Database model interfaces

### NzbDrone.Common.*
- `NzbDrone.Common.Http` - HTTP client interfaces and models

## Usage

The CI setup scripts automatically build these stubs and use them as references for the Brainarr plugin build. The process is:

1. Build stub assemblies: `dotnet build LidarrStubs.sln`
2. Copy generated DLLs to mock-lidarr/bin/
3. Build Brainarr plugin against stub assemblies

## Benefits

- **Faster Builds**: Compile much faster than full Lidarr source
- **Reliable**: Provide actual .NET metadata instead of empty files
- **Maintainable**: Easy to add new types as plugin requirements evolve
- **Cross-Platform**: Work on all CI environments (Windows, Linux, macOS)

## Implementation Notes

- All stub types are minimal implementations containing only what's needed for compilation
- No business logic is implemented - these are purely for satisfying compiler references
- Dependencies are kept minimal (only FluentValidation and NLog as needed)
- Assembly names match actual Lidarr assemblies (Lidarr.Core, Lidarr.Common)
- Root namespace matches Lidarr's structure (NzbDrone.*)

## Adding New Types

If the plugin requires additional Lidarr types:

1. Add the required interfaces/classes to the appropriate stub project
2. Ensure minimal implementation (no business logic)
3. Update project references if new dependencies are needed
4. Test local build to ensure it compiles

## CI Integration

These stubs are automatically used by the CI setup scripts:
- `scripts/setup-ci-lidarr.sh` (bash for Linux/macOS)
- `scripts/setup-ci-lidarr.ps1` (PowerShell for Windows)

The scripts use a three-tier fallback approach:
1. **Preferred**: Build and use these stub assemblies
2. **Fallback**: Use actual Lidarr assemblies if available
3. **Last Resort**: Create empty placeholder files