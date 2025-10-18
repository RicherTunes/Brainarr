# Building Brainarr Plugin

## Prerequisites

1. **.NET SDK 6.0+ (8.0 recommended)** — the plugin targets `net6.0`, and the repo pins SDK `8.0.x` via `global.json` for tooling consistency.
1. **Real Lidarr assemblies** — the build targets real Lidarr binaries (no stubs). The setup scripts fetch them for you.
1. **Git** — to clone/update this repository.

## Quick Build

### Option 1: One‑command setup (Recommended)

Run the repository bootstrap to fetch Lidarr nightly/plugin assemblies (via Docker when available), restore, and build:

```bash
# Windows (PowerShell)
./setup.ps1

# macOS/Linux (Bash)
chmod +x ./setup.sh
./setup.sh
```

This prepares `ext/Lidarr-docker/_output/net6.0/` (or `ext/Lidarr/_output/net6.0/` when Docker is unavailable) and sets `LIDARR_PATH` for subsequent builds. After setup:

```bash
cd Brainarr.Plugin
dotnet build -c Release
```

### Option 2: Use an explicit `LIDARR_PATH`

If your Lidarr assemblies live elsewhere, set the `LIDARR_PATH` environment variable before building:

**Windows:**

```cmd
set LIDARR_PATH=C:\ProgramData\Lidarr\bin
cd Brainarr.Plugin
dotnet build -c Release
```

**Linux/macOS:**

```bash
export LIDARR_PATH=/opt/Lidarr
cd Brainarr.Plugin
dotnet build -c Release
```

**PowerShell:**

```powershell
$env:LIDARR_PATH = "C:\\ProgramData\\Lidarr\\bin"
cd Brainarr.Plugin
dotnet build -c Release
```

## Common Lidarr Assembly Locations

### Windows

- **Installer**: `C:\ProgramData\Lidarr\bin`
- **Portable**: `[Lidarr folder]\bin`
- **Scoop**: `%USERPROFILE%\scoop\apps\lidarr\current`

### Linux

- **Package Manager**: `/usr/lib/lidarr/bin`
- **Manual Install**: `/opt/Lidarr`
- **Snap**: `/snap/lidarr/current`

### Docker

- **Bootstrap output (default)**: `ext/Lidarr-docker/_output/net6.0/`
- **Inside running container**: `/app/bin` (extracted by scripts into the path above)

## Build Output

After successful build, the plugin files will be in:

```text
Brainarr.Plugin/bin/
├── Lidarr.Plugin.Brainarr.dll    # Main plugin
├── plugin.json                   # Plugin manifest
├── [dependencies].dll            # NuGet packages
```

## Build, Package, and Deploy

Use `build.ps1` / `build.sh` for common flows:

```powershell
# Windows (PowerShell)
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"

# Build + test/package/deploy
./build.ps1 -Test         # Build + test
./build.ps1 -Package      # Build + package (ZIP)
./build.ps1 -Deploy       # Deploy to local Lidarr plugins folder
```

```bash
# macOS/Linux (Bash)
./build.sh                # Build
./build.sh --test         # Build + test
./build.sh --package      # Build + package (tar.gz)
./build.sh --deploy       # Deploy to local Lidarr plugins folder
```

## Troubleshooting

### Error: "Lidarr installation not found"

- Set `LIDARR_PATH` environment variable to your Lidarr installation
- Verify Lidarr DLL files exist in the specified path
- Verify you have Lidarr nightly/plugins assemblies (minimum `2.14.2.4786`).

### Error: "Could not load file or assembly"

- Ensure you're using .NET SDK 6.0+ (8.0 recommended)
- Verify Lidarr assemblies are compatible version
- Try cleaning and rebuilding: `dotnet clean && dotnet build`

### Error: "Access denied" or permission issues

- Run command prompt as administrator (Windows)
- Check file permissions on Lidarr directory
- Ensure Lidarr is not running during build

## Development Setup

For development with full test suite:

1. Extract/setup repository
1. Build main plugin: `dotnet build Brainarr.Plugin`
1. Run tests: `dotnet test Brainarr.Tests`

### Internals You May Touch

- Provider/model keys: use `Services/Core/ModelKeys.cs` (`ModelKey`) for per-model isolation (limiters, breakers, metrics).
- Resilience registries: `Services/Resilience/LimiterRegistry.cs` and `Services/Resilience/BreakerRegistry.cs` provide shared per‑model guards.
- Model mapping: `Configuration/ModelIdMappingValidator.cs` runs at startup (warn‑only) to catch alias drift/duplicates.
- Metrics: model‑aware latency/error metrics are emitted via two paths:
  - `PerformanceMetrics.RecordProviderResponseTime("{provider}:{model}", duration)` — internal snapshot used by scoreboards.
  - `MetricsCollector` records label‑based series via `ProviderMetricsHelper`:
    - Latency (seconds): `provider_latency_seconds{provider,model}`
    - Errors: `provider_errors_total{provider,model}`
    - Throttles (HTTP 429): `provider_throttles_total{provider,model}`
- Structured outputs: toggle with `PreferStructuredJsonForChat` in settings; providers consult capabilities and apply `StructuredJsonValidator` repair before parsing.

## CI/CD Notes

CI compiles against the Lidarr plugins branch by extracting assemblies from Docker image `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}` into `ext/Lidarr-docker/_output/net6.0/`. Jobs fail fast if assemblies are missing. See `.github/workflows/` for `sanity-build`, `plugin-package`, and test jobs.

## Tests and Coverage

Run the full suite locally with the helper script:

```powershell
./test-local-ci.ps1
```

Skip downloading Lidarr assemblies if you already have `ext/Lidarr/_output/net6.0`:

```powershell
./test-local-ci.ps1 -SkipDownload
```

For faster, more stable local runs (exclude heavy Performance/Stress tests) and generate an HTML coverage report:

```powershell
./test-local-ci.ps1 -SkipDownload -ExcludeHeavy -GenerateCoverageReport -InstallReportGenerator
```

This produces a Cobertura file under `TestResults/` and an HTML report at:

```
TestResults/CoverageReport/index.html
```

To generate a report from an existing test run:

```powershell
scripts/generate-coverage-report.ps1 -InstallTool
```

If you already have ReportGenerator installed globally (`dotnet tool install -g dotnet-reportgenerator-globaltool`), omit `-InstallTool`.

## Observability & Metrics (Advanced)

Brainarr now emits model‑aware metrics and provides lightweight previews for debugging performance and reliability. These features are available to power users under Advanced settings and via internal actions.

### What’s Collected

- Latency histogram per `{provider}:{model}` (p50/p95/p99, avg, count)
- Error counters per `{provider}:{model}`
- Throttle (HTTP 429) counters per `{provider}:{model}`

### Where to See It

- In UI (for maintainers): Import List → Brainarr → Advanced → `Observability (Preview)`
  - Displays top provider:model series for the last 15 minutes.
  - Help link opens a compact HTML table (see below).
  - Screenshot: `docs/assets/observability-preview.png` (add your screenshot here)

### Actions (internal)

- `observability/get` — JSON summary (last 15 minutes)
- `observability/getoptions` — options for the TagSelect preview
- `observability/html` — compact HTML preview (table)
- `metrics/prometheus` — Prometheus‑formatted export

Tip: To quickly hide the preview before release, set `EnableObservabilityPreview = false` (hidden, Advanced). The UI field and endpoints will be disabled without code edits.

Note: These actions are invoked by the plugin UI. For manual testing, call the plugin’s import list action endpoint with the corresponding `action` value (per host’s internal conventions).

### Adaptive Throttling (Hidden, Off by Default)

If enabled, Brainarr temporarily reduces per‑model concurrency after HTTP 429 responses and gradually restores it during the throttle window.

- Settings (Advanced, Hidden):
  - `EnableAdaptiveThrottling` (default: false)
  - `AdaptiveThrottleSeconds` (default: 60)
  - `AdaptiveThrottleCloudCap` (default: 2 if unset)
  - `AdaptiveThrottleLocalCap` (default: 8 if unset)

- Behavior:
  - Respects `Retry-After` header to size the throttle TTL when present (seconds or HTTP-date), clamped to 5s–5m.
  - Without `Retry-After`, uses `AdaptiveThrottleSeconds`.
- Emits `provider_throttles_total{provider,model}` counters and decays concurrency back to defaults within the window.

### Per‑Model Concurrency Overrides (Hidden)

- `MaxConcurrentPerModelCloud` and `MaxConcurrentPerModelLocal` allow power users to set upper bounds without code changes.

### Implementation Notes

- Model identity: `Services/Core/ModelKeys.cs` (`ModelKey`) — used by limiters/breakers/metrics.
- Limiters: `Services/Resilience/LimiterRegistry.cs` — includes adaptive throttle/decay.
- Breakers: `Services/Resilience/BreakerRegistry.cs`.
- Observability endpoints: `Services/Core/BrainarrOrchestrator.cs`.
- Prometheus export: `Services/Telemetry/MetricsCollector.cs`.
