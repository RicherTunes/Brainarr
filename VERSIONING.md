# Versioning — Brainarr

## Source of truth: `VERSION` file

The single source of truth for the plugin version is the top-level `VERSION` file
(e.g. `1.5.3`).  All other version references are derived from it automatically or
updated by the release workflow; they must **never** be edited by hand.

| Artifact | How it gets the version | Do not edit manually |
|---|---|---|
| `VERSION` | **Source of truth** — edit this one | — |
| Assembly `InformationalVersion` | `Directory.Build.props` reads `VERSION` via `$([System.IO.File]::ReadAllText(...))` | yes |
| `plugin.json` `.version` | Release workflow `sed`-patches it from the git tag | yes |
| `manifest.json` `.version` | Release workflow `sed`-patches it from the git tag | yes |

## Wiring

`Directory.Build.props` (repo root) contains:

```xml
<VersionFromFile Condition="Exists('$(MSBuildThisFileDirectory)VERSION')">
  $([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)VERSION').Trim())
</VersionFromFile>
<Version Condition="'$(Version)' == '' And '$(VersionFromFile)' != ''">$(VersionFromFile)</Version>
```

The csproj does not hardcode `<Version>`.  The assembly informational version flows
from `VERSION` → `Directory.Build.props` → SDK-generated `AssemblyInfo.cs`.

## Bumping a version

1. Edit `VERSION` with the new semver string.
2. Push a git tag `v<VERSION>`.
3. The release workflow patches `plugin.json` and `manifest.json` automatically.

## Drift risk

`plugin.json` and `manifest.json` are patched by the release workflow sed step, not
by the build.  If a developer changes them in a PR they will be overwritten at release
time.  Do not rely on in-repo values being accurate between releases.
