# Brainarr Packaging Policy Baseline

This repo follows the Lidarr plugin ecosystem packaging rules:

## Ship (runtime deps)
These assemblies must be shipped as separate files alongside the plugin DLL.

- `Lidarr.Plugin.Abstractions.dll`

## Merge (internalize)
These should be merged/internalized into `Lidarr.Plugin.Brainarr.dll` (or otherwise not shipped as separate files).

- `Lidarr.Plugin.Common.dll`
- `Polly*`, `TagLibSharp*`, and other non-type-identity dependencies

## Do Not Ship (host provides)
These must never be included in the plugin package.

- `Lidarr.Core.dll`, `Lidarr.Common.dll`, `Lidarr.Host.dll`, `Lidarr.Http.dll`, etc.
- `NzbDrone.*.dll`
- `System.Text.Json.dll` (cross-boundary type identity risk)
- `FluentValidation.dll`
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
- `Microsoft.Extensions.Logging.Abstractions.dll`

## How it is enforced
- Unit tests: `Brainarr.Tests/Packaging/BrainarrPackagingPolicyTests.cs`
  - Local: tests skip if no package exists.
  - CI/strict: set `REQUIRE_PACKAGE_TESTS=true` and provide `PLUGIN_PACKAGE_PATH` to require the package.
