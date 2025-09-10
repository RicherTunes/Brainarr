# Building Brainarr Plugin

## Prerequisites

1. **.NET 6.0 SDK** or later
1. **Lidarr installation** (the plugin needs Lidarr assemblies to compile)
1. **Git** (to clone the repository)

## Quick Build

### Option 1: Automatic Detection (Recommended)

The build system will automatically detect Lidarr in common installation paths:

```bash
# Extract or clone the project
cd Brainarr
cd Brainarr.Plugin
dotnet build -c Release
```

### Option 2: Environment Variable

If Lidarr is installed in a custom location, set the `LIDARR_PATH` environment variable:

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
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"
cd Brainarr.Plugin
dotnet build -c Release
```

## Common Lidarr Installation Paths

### Windows

- **Installer**: `C:\ProgramData\Lidarr\bin`
- **Portable**: `[Lidarr folder]\bin`
- **Scoop**: `%USERPROFILE%\scoop\apps\lidarr\current`

### Linux

- **Package Manager**: `/usr/lib/lidarr/bin`
- **Manual Install**: `/opt/Lidarr`
- **Snap**: `/snap/lidarr/current`

### Docker

- **Host Path**: Map container `/usr/lib/lidarr/bin` to host
- **Inside Container**: `/usr/lib/lidarr/bin`

## Build Output

After successful build, the plugin files will be in:

```text
Brainarr.Plugin/bin/
├── Lidarr.Plugin.Brainarr.dll    # Main plugin
├── plugin.json                   # Plugin manifest
├── [dependencies].dll            # NuGet packages
```

## Using build_and_deploy.ps1

For convenience, use the included PowerShell script:

```powershell
# Set Lidarr path if needed
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"

# Build and deploy
.\build_and_deploy.ps1
```

This script will:

1. Build the plugin in Release mode
1. Copy files to a deployment folder
1. Create a ZIP package for distribution

## Troubleshooting

### Error: "Lidarr installation not found"

- Set `LIDARR_PATH` environment variable to your Lidarr installation
- Verify Lidarr DLL files exist in the specified path
- Check that you have Lidarr v1.0+ installed

### Error: "Could not load file or assembly"

- Ensure you're using .NET 6.0 SDK
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
  - `Services/Telemetry/ProviderMetricsHelper.cs` builds names for `MetricsCollector` (e.g., `provider.latency.openai.gpt-4o-mini`, `provider.errors.openai.gpt-4o-mini`).
- Structured outputs: toggle with `PreferStructuredJsonForChat` in settings; providers consult capabilities and apply `StructuredJsonValidator` repair before parsing.

## CI/CD Notes

For automated builds, set the `LIDARR_PATH` environment variable in your CI system:

**GitHub Actions:**

```yaml
env:
  LIDARR_PATH: /opt/Lidarr
```

**Docker Build:**

```dockerfile
ENV LIDARR_PATH=/usr/lib/lidarr/bin
```

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
  - Emits `provider.429.{provider}.{model}` counters and decays concurrency back to defaults within the window.

### Per‑Model Concurrency Overrides (Hidden)

- `MaxConcurrentPerModelCloud` and `MaxConcurrentPerModelLocal` allow power users to set upper bounds without code changes.

### Implementation Notes

- Model identity: `Services/Core/ModelKeys.cs` (`ModelKey`) — used by limiters/breakers/metrics.
- Limiters: `Services/Resilience/LimiterRegistry.cs` — includes adaptive throttle/decay.
- Breakers: `Services/Resilience/BreakerRegistry.cs`.
- Observability endpoints: `Services/Core/BrainarrOrchestrator.cs`.
- Prometheus export: `Services/Telemetry/MetricsCollector.cs`.
