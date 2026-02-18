# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Brainarr is a **production-ready** multi-provider AI-powered import list plugin for Lidarr that generates intelligent music recommendations. The project supports 9 different AI providers ranging from privacy-focused local models to powerful cloud services.

## Runtime & Docker Image Requirements (CRITICAL)

**Target framework**: `net8.0` — all plugins MUST target .NET 8.

**Lidarr Docker image**: Use ONLY a `.NET 8` plugins-branch image. The current pinned tag is:

```text
LIDARR_DOCKER_VERSION=pr-plugins-3.1.2.4913
```

- Image: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`
- Digest: `sha256:ae0b3b14769fdfeb73fe5d9e61ebcda04edf202244bcbd6323d2fe1381154f57`
- Pinned in: `.github/lidarr_digest.txt` and `scripts/extract-lidarr-assemblies.sh`

**NEVER use `pr-plugins-2.x` tags** — those are .NET 6 images. Loading a .NET 8 plugin into a .NET 6 host causes `System.Runtime` assembly load failures and Lidarr crash-loops. The guardrail in `extract-lidarr-assemblies.sh` will catch this (fails if `System.Runtime.dll` major != 8).

When bumping the Docker image tag, update ALL of these locations:
- All `.github/workflows/*.yml` files referencing `LIDARR_DOCKER_VERSION`
- `.github/lidarr_digest.txt`
- `scripts/extract-lidarr-assemblies.sh` (default fallback)
- `scripts/snapshots/run-local.sh` and `run-local.ps1`
- `test-local-ci.sh`

## Plugin Packaging Policy (CRITICAL)

**The plugin package MUST contain:**
- `Lidarr.Plugin.Brainarr.dll` - Main plugin
- `Lidarr.Plugin.Abstractions.dll` - Required for plugin discovery (host does not ship it)
- `plugin.json`
- `manifest.json`

**The plugin package MUST NOT contain host-provided contract assemblies (type-identity conflicts):**
- `FluentValidation.dll`
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
- `Microsoft.Extensions.Logging.Abstractions.dll`
- `NLog.dll`
- `System.Text.Json.dll`
- `Lidarr.*.dll` (non-plugin host assemblies)
- `NzbDrone.*.dll`

**The plugin must reference host versions of contract packages.** Use the host-version coupling tests and `scripts/check-host-versions.ps1` to keep NuGet versions aligned with the Lidarr host.

**Do not rely on copying host-provided contract assemblies into the plugin output:**

```xml
<!-- OK for compile-time (runtime resolves from host); packaging MUST exclude the DLL -->
<PackageReference Include="FluentValidation" />
```

**Validation:** Run `./build.ps1 -Package` and verify the zip contains the required DLLs.

## Development Status

**Current Status**: Production-ready v1.0.0 - Full implementation with comprehensive test suite

The project includes:

- Complete implementation with 9 AI providers (2 local options, 7 cloud providers)
- Comprehensive test suite (33+ test files)
- Production-ready architecture with advanced features
- Full documentation in `docs/` folder

## Architecture Overview

The implemented architecture includes:

### Multi-Provider AI System

- **Local-First Options**: Privacy-focused local providers (Ollama, LM Studio)
- **Cloud Integration**: 9 total providers including OpenAI, Anthropic, Google Gemini, etc.
- **Provider Failover**: Automatic failover with health monitoring
- **Dynamic Detection**: Auto-detects available models for local providers

### Implemented Architecture

```text
Brainarr.Plugin/
├── Configuration/          # Provider settings and validation
│   ├── Constants.cs
│   ├── ProviderConfiguration.cs
│   └── Providers/          # Per-provider configuration classes
├── Services/
│   ├── Core/              # Core orchestration services
│   │   ├── AIProviderFactory.cs
│   │   ├── AIService.cs
│   │   ├── LibraryAnalyzer.cs
│   │   └── ProviderRegistry.cs
│   ├── Providers/         # AI provider implementations (9 providers)
│   ├── Support/           # Supporting services
│   ├── LocalAIProvider.cs
│   ├── ModelDetectionService.cs
│   ├── ProviderHealth.cs
│   ├── RateLimiter.cs
│   ├── RecommendationCache.cs
│   └── RetryPolicy.cs
├── BrainarrImportList.cs  # Main Lidarr integration
└── BrainarrSettings.cs    # Configuration UI

Brainarr.Tests/            # Comprehensive test suite
├── Configuration/         # Configuration tests
├── Services/Core/         # Core service tests
├── Services/              # Provider tests
├── Integration/           # End-to-end tests
└── EdgeCases/            # Edge case handling
```

### Key Technical Patterns

- **Provider Pattern**: Each AI service implements `IAIProvider` interface
- **Factory Pattern**: `AIProviderFactory` manages provider instantiation
- **Registry Pattern**: `ProviderRegistry` for extensible provider management
- **Health Monitoring**: Real-time provider availability tracking
- **Rate Limiting**: Per-provider rate limiting with configurable limits
- **Caching**: Intelligent recommendation caching to reduce API calls
- **Retry Policies**: Exponential backoff retry with circuit breaker patterns
- **Recommendation Modes**: Supports both artist-only and album-specific recommendations
- **Cross-Platform**: Windows, macOS, and Linux compatibility with platform-specific optimizations

## Implemented Features

### Core Functionality

- ✅ 9 AI providers (local + cloud)
- ✅ Auto-detection of local models
- ✅ Provider health monitoring
- ✅ Rate limiting and caching
- ✅ Comprehensive configuration validation with timeout bounds (5-600s)
- ✅ Library analysis and profiling
- ✅ Recommendation sanitization
- ✅ **Artist-only recommendation mode** - Import all albums by recommended artists
- ✅ **Dual recommendation modes** - Artists vs. specific albums
- ✅ **Music styles catalog** - Normalization, matching, and filtering
- ✅ **Circuit breaker pattern** - Prevents cascading failures with configurable thresholds

### Technology Stack

- **Platform**: .NET 6+ (Lidarr plugin framework)
- **HTTP Client**: Lidarr's IHttpClient for provider communication
- **Configuration**: Lidarr's field definition system with validation
- **Logging**: NLog integration with structured logging
- **Testing**: Comprehensive test suite covering all components

## Development Workflow

For ongoing development:

1. **Build**: `dotnet build`
2. **Test**: `dotnet test` (33 test files)
3. **Deploy**: Copy to Lidarr plugins directory
4. **Debug**: Enable debug logging in Lidarr settings

### Common Development Commands

```bash
# Build plugin
dotnet build -c Release

# Run full test suite
dotnet test

# Run specific test categories
dotnet test --filter Category=Integration
dotnet test --filter Category=EdgeCase

# Package for deployment
dotnet publish -c Release
```

## CI/CD Pipeline Solution

**RESOLVED**: The GitHub Actions CI was previously failing due to missing Lidarr assembly dependencies. This has been definitively solved using the plugins Docker image extraction approach.

### The Working Solution

The CI workflow now uses pre-built Lidarr assemblies instead of trying to build from source:

```yaml
- name: Download Lidarr Assemblies
  run: |
    echo "Downloading Lidarr assemblies from latest release..."
    mkdir -p ext/Lidarr/_output/net8.0

    # Get latest release URL dynamically
    LIDARR_URL=$(curl -s https://api.github.com/repos/Lidarr/Lidarr/releases/latest | grep "browser_download_url.*linux-core-x64.tar.gz" | cut -d '"' -f 4 | head -1)

    if [ -n "$LIDARR_URL" ]; then
      curl -L "$LIDARR_URL" -o lidarr.tar.gz
    else
      # Fallback to known version
      curl -L "https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz" -o lidarr.tar.gz
    fi

    tar -xzf lidarr.tar.gz

    # Copy required assemblies
    cp Lidarr/Lidarr.Core.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Common.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Http.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Api.V1.dll ext/Lidarr/_output/net8.0/
```

### Why This Works

1. **Stable Dependencies**: Uses official Lidarr release binaries that are tested and stable
2. **No Compilation**: Eliminates complex build dependencies and potential source compilation failures
3. **Fast & Reliable**: Simple download and extract, much faster than building from source
4. **Cross-Platform**: Works identically across Ubuntu, Windows, and macOS runners
5. **Maintainable**: Clear, understandable workflow that's easy to debug

### Key Learnings

- **Never build Lidarr from source in CI** - it's complex, slow, and error-prone
- **Use pre-built assemblies** - download from GitHub releases instead
- **Dynamic URL detection** - use GitHub API to get latest release URLs with fallbacks
- **Proper error handling** - validate downloads and extractions with clear error messages
- **Cross-platform testing** - test on multiple OS/runtime combinations

### Local Development Setup

The project's `.csproj` file has sophisticated Lidarr path resolution that automatically finds assemblies in:
1. Command line: `-p:LidarrPath=...`
2. Environment: `LIDARR_PATH`
3. Local submodule: `ext/Lidarr/_output/net8.0`
4. System installations: `/opt/Lidarr`, `C:\ProgramData\Lidarr\bin`, etc.

For local development, ensure Lidarr assemblies are present in `ext/Lidarr/_output/net8.0/` or set the `LIDARR_PATH` environment variable.

### CI Status: ✅ WORKING

The CI pipeline now successfully:
- ✅ Downloads Lidarr assemblies from GitHub releases
- ✅ Builds plugin across 6 environments (Ubuntu/Windows/macOS × .NET 6.0.x/8.0.x)
- ✅ Runs comprehensive test suite (33 test files)
- ✅ Performs security analysis with CodeQL
- ✅ Creates release packages on tagged releases

**This solution has been UPGRADED with TypNull's proven Docker approach and should not require further changes.**

### Latest Improvements (Based on TypNull's Tubifarry Plugin)

The CI has been enhanced with the proven Docker extraction method:

**Key Improvements:**
- ✅ **Docker Assembly Extraction**: Uses `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` (plugins branch)
- ✅ **Minimal NuGet.config**: Eliminates private feed authentication issues
- ✅ **CI-Optimized Project File**: Fallback approach with essential dependencies only
- ✅ **Consistent Across All Jobs**: Same Docker approach for build, test, security scan, and release
- ✅ **Environment Variables**: Centralized configuration for maintainability

**Why This Works Better:**
1. **Plugins Branch Compatibility**: Uses actual plugins branch assemblies instead of main branch
2. **No Private NuGet Issues**: Avoids Servarr Azure DevOps feed authentication problems
3. **Proven Success**: Based on first successful Lidarr plugin CI (TypNull's Tubifarry)
4. **More Reliable**: Docker extraction is more stable than building from source

### Update (September 2025)

- Use the Lidarr plugins Docker image (`ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}`) to extract assemblies in CI and local scripts.
- The older release-tarball instructions below are retained for historical context only and should not be used for main CI paths.

## Local Verification (Billing-Blocked CI)

When GitHub Actions billing is blocked, run the merge-critical verification pipeline locally:

```bash
pwsh scripts/verify-local.ps1                    # Full pipeline (extract + build + package + closure + E2E)
pwsh scripts/verify-local.ps1 -SkipExtract       # Fast rerun (reuse cached host assemblies)
pwsh scripts/verify-local.ps1 -SkipTests         # Build + packaging closure only
pwsh scripts/verify-local.ps1 -NoRestore         # Skip dotnet restore (fast iteration)
pwsh scripts/verify-local.ps1 -IncludeSmoke      # + Docker smoke test (mounts plugin in Lidarr)
```

**Prerequisites**: PowerShell 7+ (`pwsh`), .NET 8 SDK, Docker (for extract/smoke stages).

The script delegates to `ext/Lidarr.Plugin.Common/scripts/local-ci.ps1`, which orchestrates the same gates as CI: host assembly extraction with .NET 8 + FV 9.5.4 guardrails, plugin packaging via `New-PluginPackage`, and packaging closure validation via `generate-expected-contents.ps1 -Check`.

## Local Development Setup

1. **Prerequisites**:
   - .NET 6+ SDK
   - Lidarr development environment
   - At least one AI provider (Ollama recommended for testing)

2. **Development Environment**:
   - IDE: Visual Studio, VS Code, or JetBrains Rider
   - Testing: Local Lidarr instance for plugin testing
   - AI Providers: Local Ollama installation recommended

## Security Considerations

- API keys stored securely through Lidarr's configuration system
- Local providers prioritized to avoid data transmission
- No sensitive music library data logged or transmitted unnecessarily
- Rate limiting and error handling for cloud providers

## Specialist Development Guidance

Based on the task context, apply the appropriate specialist expertise:

### 🤖 AI Provider System Specialist

**When working with**: provider implementations, failover logic, model detection, health monitoring

**Key Focus Areas**:
- **IAIProvider Interface**: Follow established contract in `Services/Providers/`
- **Provider Registry**: Use `ProviderRegistry.cs` patterns for extensible provider management
- **Health Monitoring**: Implement `ProviderHealth.cs` patterns for availability tracking
- **Model Detection**: Follow `ModelDetectionService.cs` for local provider auto-detection
- **Authentication**: Provider-specific auth patterns in `Configuration/Providers/`
- **Rate Limiting**: Per-provider limits using `RateLimiter.cs` patterns
- **Error Handling**: Implement proper retry policies with circuit breaker patterns

**Implementation Patterns**:

```csharp
// Provider implementation template
public class NewProvider : IAIProvider
{
    public async Task<bool> TestConnectionAsync() { /* Health check */ }
    public async Task<List<string>> GetAvailableModelsAsync() { /* Model detection */ }
    public async Task<List<ImportListItemInfo>> GetRecommendationsAsync() { /* Core logic */ }
}
```

### ⚙️ Configuration System Specialist

**When working with**: settings, validation, UI integration, provider configuration

**Key Focus Areas**:
- **Dynamic UI**: Use Lidarr's field definition system with conditional visibility
- **Validation**: FluentValidation patterns in `ProviderConfiguration.cs`
- **Settings Classes**: Provider-specific configuration in `Configuration/Providers/`
- **Backwards Compatibility**: Migration patterns for configuration changes
- **UI Integration**: Follow `BrainarrSettings.cs` patterns for seamless UX

**Implementation Patterns**:

```csharp
// Configuration validation template
When(c => c.Provider == AIProvider.NewProvider, () =>
{
    RuleFor(c => c.NewProviderApiKey)
        .NotEmpty()
        .WithMessage("API key is required for NewProvider");
});
```

### 🧪 Testing & Quality Specialist

**When working with**: tests, mocking, coverage, quality assurance

**Key Focus Areas**:
- **Test Structure**: Follow `Brainarr.Tests/` organization patterns
- **Test Categories**: Use `[Trait("Category", "...")]` for Integration, EdgeCase, Unit
- **Mocking Strategy**: Consistent mocking with Moq framework
- **Edge Cases**: Comprehensive error condition testing
- **Integration Tests**: End-to-end workflow validation
- **Performance Tests**: Provider response time and resource usage

**Implementation Patterns**:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task Provider_Should_HandleFailover_WhenPrimaryUnavailable()
{
    // Arrange: Mock primary provider failure
    // Act: Trigger failover scenario
    // Assert: Verify secondary provider usage
}
```

### 🔧 CI/CD & Build Specialist

**When working with**: GitHub Actions, builds, deployment, dependencies

**Key Focus Areas**:
- **Assembly Management**: NEVER build Lidarr from source - use pre-built assemblies
- **Cross-Platform**: Test on Ubuntu/Windows/macOS with .NET 6.0.x/8.0.x matrix
- **Dependency Resolution**: Follow `.csproj` Lidarr path resolution patterns
- **Security Scanning**: CodeQL integration and vulnerability assessment
- **Release Automation**: Tagged release packaging and asset distribution

**Critical Rules**:
- ❌ Never: `git submodule update --recursive` (complex, error-prone)
- ✅ Always: Download pre-built Lidarr assemblies from GitHub releases
- ✅ Always: Use matrix strategy for comprehensive platform testing
- ✅ Always: Validate assembly downloads with proper error handling

### ⚡ Performance & Architecture Specialist

**When working with**: optimization, caching, memory management, scalability

**Key Focus Areas**:
- **Caching Strategy**: Intelligent caching with `RecommendationCache.cs`
- **Rate Limiting**: Provider-specific limits with `RateLimiter.cs`
- **Memory Management**: Efficient object lifecycle in provider implementations
- **Async Patterns**: Proper async/await usage for I/O operations
- **Circuit Breaker**: Resilient failure handling with `RetryPolicy.cs`
- **Resource Optimization**: HTTP client reuse and connection pooling

**Performance Patterns**:

```csharp
// Efficient caching pattern
var cacheKey = $"{provider}:{libraryProfile}:{timestamp}";
if (cache.TryGetValue(cacheKey, out var cached))
    return cached;
```

### 📚 Documentation & User Experience Specialist

**When working with**: documentation, user guides, API docs, code comments

**Key Focus Areas**:
- **Technical Accuracy**: Keep documentation synchronized with implementation
- **User Configuration**: Clear setup guides for each provider type
- **API Documentation**: Comprehensive provider interface documentation
- **Code Comments**: Meaningful inline documentation for complex logic
- **Architecture Docs**: Maintain `docs/` folder technical documentation

**Documentation Standards**:
- Use consistent terminology across all documentation
- Include practical examples for configuration patterns
- Maintain backwards compatibility notes for breaking changes
- Document security considerations for each provider type

## Context-Sensitive Activation

Claude Code will automatically apply the appropriate specialist context based on:

- **Provider/AI/Failover mentions** → AI Provider System Specialist
- **Config/Settings/Validation mentions** → Configuration System Specialist
- **Test/Mock/Coverage mentions** → Testing & Quality Specialist
- **CI/Build/Deploy mentions** → CI/CD & Build Specialist
- **Performance/Cache/Memory mentions** → Performance & Architecture Specialist
- **Docs/README/Comments mentions** → Documentation & UX Specialist
- Always use gh or git commands to validate the status of a build or any other validations.
- NEVER MERGE FIRST, TEST SECOND - Always test changes in their branch before merging to main. Even "safe-looking"
  dependency updates can introduce breaking changes, version conflicts, or runtime incompatibilities.

## Flaky Tests Policy

**Flaky tests are priority tech debt that must be paid immediately.** A test that passes sometimes and fails sometimes erodes trust in the entire test suite. When a flaky test is discovered:

1. **Fix it before starting new feature work** — flaky tests block reliable CI
2. **Document the root cause** in a commit message so the pattern is not repeated
3. **Never skip or disable** a flaky test without a tracking issue

### Known Flaky Tests (Brainarr)

| Test | Root Cause | Fix |
|------|-----------|-----|
| `E2EHermeticGateTests.LogRedaction_*` | **Fixed.** NLog config race — tests use `TestLogger.Create()` which mutates global `LogManager.Configuration` but class lacked `[Collection("LoggingTests")]`, allowing parallel execution with other NLog tests. | Added `[Collection("LoggingTests")]` to `E2EHermeticGateTests`. |
| `LoggerWarnOnceTests.WarnOnceWithEvent_Logs_OnlyOnce_PerKey` | **Fixed.** Static `_warnOnceKeys` dictionary persists across tests — if another test used the same event+key combo, this test sees 0 logs. | Added `LoggerExtensions.ClearWarnOnceKeysForTests()` call in constructor. |
| `BrainarrOrchestratorTopUpTests.FetchRecommendations_WithTopUpEnabled_FillsToTarget` | **Fixed.** Two issues: (1) `MusicBrainzResolver.EnrichWithMbidsAsync` catch block silently dropped recommendations on HTTP failure instead of preserving them (production bug). (2) Test created real resolvers that hit the MusicBrainz API, making results non-deterministic. | Production fix: added `result.Add(rec)` in catch block. Test fix: injected pass-through mock resolvers to eliminate external HTTP calls. |
| `LibraryAnalyzerTests.AnalyzeLibrary_DeterminesDiscoveryTrend` | **Fixed.** `CreateArtist` helper used `new Random().Next(1, 1000)` for IDs — birthday-problem collisions (~1%) caused `ToDictionary` to throw `ArgumentException`, caught by `AnalyzeLibrary`'s catch-all which returns fallback "stable collection" instead of "rapidly expanding". | Replaced `new Random().Next()` with `Interlocked.Increment(ref _nextArtistId)` for deterministic unique IDs. |
| `RateLimiter_WithThreadPoolExhaustion_StillEnforcesLimits` | **Fixed.** Too many requests (20) at too high a rate (10/sec) with too short a timeout (10s) allowed thread pool starvation to cause `TaskCanceledException`. | Reduced to 10 requests at 5/sec with 30s timeout (commit 146b1fe). |
| `Performance_enhanced_cache_within_10pct_of_basic` | **Fixed.** JIT warmup (PR #508) was insufficient — sync `TryGet` completes in sub-millisecond, making relative comparison to async `GetAsync` volatile. | Dual threshold: 10x relative OR 50ms absolute floor, whichever is more generous. 5/5 local stress passes. |

### Quarantined Tests (OOM — crash test host)

These stress tests allocate large datasets that exhaust test host memory. They are excluded from default runs via `[Trait("State", "Quarantined")]` but remain discoverable via `--filter "State=Quarantined"`.

| Test | File |
|------|------|
| `Cache_Should_HandleMillionOperations` | SecurityTestSuite.cs |
| `Cache_UnderMemoryPressure_EvictsOldEntries` | ResourceAndTimeTests.cs |
| `Cache_WithVeryLargeData_HandlesMemoryPressure` | CacheAndConcurrencyTests.cs |
| `StressTest_MemoryPressure_HandlesGracefully` | EnhancedConcurrencyTests.cs |
| `StressTest_ManyRecommendations` | EndToEndTests.cs |
