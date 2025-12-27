# Brainarr Packaging Policy Baseline

This repo follows the Lidarr plugin ecosystem packaging rules as defined in
`Lidarr.Plugin.Common/tests/PackageValidation/PluginPackageValidator.cs`.

## Ship (required assemblies)
These assemblies must be shipped as separate files alongside the plugin DLL:

- `Lidarr.Plugin.Abstractions.dll` (required for plugin discovery; host image does not ship it)

## Merge (internalize)
These should be merged/internalized into `Lidarr.Plugin.Brainarr.dll` via ILRepack:

- `Lidarr.Plugin.Common.dll`
- `Polly*`, `TagLibSharp*`, `Microsoft.Extensions.DependencyInjection.dll` (impl), etc.

## Do Not Ship (host provides)
These must **never** be included in the plugin package - shipping them causes type-identity conflicts:

- `FluentValidation.dll` (host provides; breaks `DownloadClient.Test(List<ValidationFailure>)`)
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll` (host provides)
- `Microsoft.Extensions.Logging.Abstractions.dll` (host provides)
- `Lidarr.Core.dll`, `Lidarr.Common.dll`, `Lidarr.Host.dll`, `Lidarr.Http.dll`, etc.
- `NzbDrone.*.dll`
- `System.Text.Json.dll` (cross-boundary type identity risk)

## How it is enforced
- Unit tests: `Brainarr.Tests/Packaging/BrainarrPackagingPolicyTests.cs`
  - Local: tests skip if no package exists.
  - CI/strict: set `REQUIRE_PACKAGE_TESTS=true` and provide `PLUGIN_PACKAGE_PATH` to require the package.
- Canonical source of truth: `Lidarr.Plugin.Common/tests/PackageValidation/PluginPackageValidator.cs`

## Known discrepancies

**TODO**: The `build.ps1` and `manifest.json` currently include FluentValidation.dll and
MS.Extensions.*Abstractions.dll in the package. These should be removed to align with the
canonical ecosystem policy. See issue tracking this alignment work.

