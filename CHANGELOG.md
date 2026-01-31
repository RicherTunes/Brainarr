# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## Quick Links
- **Documentation**: [README.md](README.md) | [Configuration](docs/CONFIGURATION.md) | [Troubleshooting](docs/TROUBLESHOOTING.md)
- **Development**: [CLAUDE.md](CLAUDE.md) | [Architecture](docs/ARCHITECTURE.md) | [Contributing](docs/CONTRIBUTING.md)
- **Ecosystem**: [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) | [Plugin Comparison](https://github.com/RicherTunes/.github/blob/main/docs/ECOSYSTEM.md)

## [Unreleased]

### Added

- **Z.AI GLM Provider**: New AI provider supporting Zhipu AI's GLM-4 series models
  - Default model: `glm-4.7-flash` (free tier available)
  - Supports `glm-4.7-flash`, `glm-4.6`, and `glm-4.5` models
  - API key authentication via [open.bigmodel.cn](https://open.bigmodel.cn)
  - Comprehensive contract tests with 239 test cases covering edge cases
  - Registry slug mapping for seamless integration
  - Model normalization through `ProviderModelNormalizer`
  - Extended negative-path contract tests
  - Edge case tests for ZaiGlm provider

- **Claude Code NDJSON Streaming**: Added NDJSON streaming support via CLI adapter
  - Supports real-time streaming responses from Claude Code CLI
  - Improved model selection UX with configurable CLI path
  - Complete redaction security with hermetic test suite

- **Provider Contract Checklist**: Documentation checklist for ship-quality provider gates

- **Layering Contract Tests**: Architecture regression tests enforcing proper component layering

- **Inner Exception Redaction**: Security enhancements for all cloud providers
  - OpenAI, Anthropic, Gemini, Perplexity, Groq, DeepSeek, OpenRouter, Z.AI GLM
  - Prevents credential leakage in error messages
  - Comprehensive security test suite with hermetic tests

- **HttpChatProviderBase**: Consolidated 4 cloud providers to shared base class
  - DeepSeek, Groq, OpenRouter, Perplexity now inherit from HttpChatProviderBase
  - Eliminated ~1,400 lines of duplicate code
  - Added 508 conformance tests ensuring all HTTP providers follow base contract

- **Graceful Provider Disable**: Providers now gracefully disable when API key is empty
  - Improved startup behavior without configuration errors
  - Better user experience for optional providers

- **FakeTimeProvider Support**: Added `FakeTimeProvider` for deterministic CircuitBreaker tests

- **Workflow Auth Lint**: CI workflow to prevent auth pattern regression

### Changed

- **Orchestrator Refactoring**: Extracted managers from `BrainarrOrchestrator` god-class
  - Improved separation of concerns and maintainability
  - Swapped _reviewQueueManager and _uiActionHandler in tests

- **Test Categories**: Migrated all tests to use `[Trait("Area")]` categorization
  - Better test organization and filtering
  - Consistent with .NET testing best practices
  - Updated test runner allowlist for Brainarr-specific categories

- **Common Library Updates**: Bumped `lidarr.plugin.common` multiple times for:
  - Streaming decoder support
  - ADR documentation fixes
  - Sanitizer improvements

- **Circuit Breaker Refactoring**: Made IBreakerRegistry injectable (WS4.2 PR0)

- **Resilience Policy**: Deleted legacy breaker implementation (WS4.2 PR3)

### Fixed

- **Test Isolation**: Fixed FormatPreferenceCache test isolation with `Clear()` method
- **Test Quarantines**: Skip timing-sensitive concurrency test and TopUp test with shared state issue
- **ZaiGlm Registry**: Fixed registry slug mapping for proper provider identification
- **Test Runner Windows**: Harden test runner against Windows build file locks
- **Packaging**: Included `manifest.json` in plugin package for entrypoint validation
- **PowerShell Parsing**: Repaired `build.ps1` parsing issues
- **Schema Format**: Updated plugin schema to canonical format
- **Submodules**: Eliminated recursive submodule calls and fixed SHA parsing
- **YAML**: Repaired broken YAML in workflows
- **TopUp Test**: Deflaked provider call verification

### Security

- All cloud providers now redact inner exceptions to prevent credential leakage
- Hermetic test suite validates redaction behavior
- Auth pattern regression via CI workflow

### Testing / CI

- Added `HttpChatProviderBaseConformanceTests` (508 tests)
- Added `ZaiGlmProviderContractTests` with comprehensive coverage
- Added `ZaiGlmSecurityContractTests` for redaction verification
- Added `LayeringContractTests` for architectural validation
- Added inner exception redaction tests for all providers
- Fixed Linux coverage collection with runsettings
- Removed redundant Final status gate causing CI failures
- Added CliWrap to NuGet PackageSourceMapping
- Adopted reusable packaging gates from Common library
- Added single-plugin E2E schema gate
- Added Performance trait to ReDoS protection tests

### Documentation

- Added Brainarr development and wiki sync guides
- Removed stale .worktrees guidance from documentation
- Updated Provider Contract Checklist for ship-quality gates

### 1.3.0 Highlights (TL;DR)

- Deterministic planning + caching: stable hashing/order, and sampling shapes move to config.
- Safer network behavior: per-request timeouts, tuned retries, better logs.
- Docs refreshed; CI/analyzers green across OSes.


## [1.3.2] - 2025-11-30

### Added

- **Claude Code Subscription Provider**: Use your Claude Code CLI credentials (`~/.claude/.credentials.json`) directly without separate API keys. Supports OAuth token refresh and expiration monitoring.
- **OpenAI Codex Subscription Provider**: Use your OpenAI Codex CLI credentials (`~/.codex/auth.json`) for seamless authentication. Supports both OAuth tokens and direct API keys.
- **SubscriptionCredentialLoader**: Cross-platform credential loading with tilde expansion, environment variable support, and automatic token expiration checking.
- **CredentialRefreshService**: Background service that monitors credential expiration and logs warnings when tokens are about to expire.
- Comprehensive unit tests for all subscription provider components (79 new tests).

### Changed

- Provider matrix now includes "Subscription" type for CLI-authenticated providers.
- Updated documentation with subscription provider configuration guide.

### Fixed

- Fixed dependency-review workflow configuration (cannot use both allow-licenses and deny-licenses).
- Fixed cross-platform test compatibility for environment variable expansion tests.

### Testing / CI

- Added unit test suites for `SubscriptionCredentialLoader`, `CredentialRefreshService`, `ClaudeCodeSubscriptionProvider`, and `OpenAICodexSubscriptionProvider`.
- Fixed test that used Windows-specific `%TEMP%` syntax to be platform-aware.

## [1.3.1] - 2025-10-19

### CI / Tooling
- Add actionlint to lint all workflows on PRs and main.
- Make Windows + .NET 6 a non-advisory matrix leg (Ubuntu + .NET 6 remains the primary gate).
- Post sticky PR comments with coverage and soft-gate PRs on >0.5% drop vs main baseline.
- Release workflows: move the moving `latest` tag to the new version and attach an SBOM.
- CI: Added `scripts/ci/check-assemblies.sh` and wired it into core workflows to fail fast when required Lidarr assemblies are missing or from the wrong source/tag.
- CI: Bumped `LIDARR_DOCKER_VERSION` to `pr-plugins-2.14.2.4786` across workflows (including nightly perf and dependency update).
- CI: Dependency update workflow now uses Docker-based assembly extraction, adds a concurrency group to avoid overlaps, and verifies assemblies with the sanity script.

### Documentation
- README: align badges/version lines and add local CI one-liners.
- Provider matrix/docs: bump headers/status strings to v1.3.1.
- README version badge and "Latest release" references updated to v1.3.1.

## [1.3.0] - 2025-09-29

### Added

- Introduced configurable `SamplingShape` defaults with advanced JSON override support so sampling ratios and relaxed-match caps are data-driven instead of hard coded.
- Added `docs/providers.yaml` plus `scripts/sync-provider-matrix.ps1` to generate the provider matrix for README, docs, and wiki from a single source of truth.

### Changed

- Hardened `LibraryAwarePromptBuilder` with a headroom guard that clamps every prompt path (including fallbacks) to `context - headroom`, trims plans when necessary, and records the reason in telemetry.
- Centralized stable hashing and deterministic ordering across planner and renderer (artist/album tie-breakers, normalized date handling) to keep prompts stable between runs and across nodes.
- Refreshed documentation for 1.3.0 (README compatibility, Advanced Settings, wiki) to reference the new provider workflow and remove duplicated guidance.

### Fixed

- Added validation for custom sampling shapes so invalid ratios or inflation values are rejected before they reach the planner.
- Ensured the plan cache sweeps expired entries before reuse, invalidates on trim events, and remains thread-safe under concurrent access.

### Testing / CI

- Expanded unit coverage for fallback headroom guards, stable hash determinism, sampling-shape defaults, plan cache concurrency, and renderer tie-breakers.

## [1.2.7] - 2025-09-24

### Added

- Ship `manifest.json` inside the release package so Lidarr recognizes Brainarr in the Installed plugins list after manual installs.

### Fixed

- Packaging script now bundles the manifest and records its hash, keeping the GitHub installer and side-load flow consistent.

## [1.2.6] - 2025-09-24

### Fixed

- Stopped shipping a private copy of FluentValidation; Brainarr now reuses Lidarr's assemblies so the `ImportListBase.Test` override matches the host signature.
- Locked the build to host-provided FluentValidation references to avoid duplicate assembly loads at runtime.

### Testing / CI

- Rebuilt the plugin and re-ran Release unit, integration, and edge-case suites against Lidarr 2.14.2 assemblies.

## [1.2.5] - 2025-09-24

### Fixed

- Ensured the build resolves Lidarr assemblies from `ext/Lidarr-docker/_output/net8.0` first so Brainarr compiles against 2.14.2+ and no longer triggers `ReflectionTypeLoadException` during Lidarr startup.
- Updated plugin metadata and docs to advertise v1.2.5 compatibility with the current Lidarr nightly baseline.

### Testing / CI

- Rebuilt the plugin and reran Release unit, integration, and edge-case suites to verify the loader fix.

## [1.2.4] - 2025-09-24

### Added

- Introduced a dedicated prompt plan cache with TTL, LRU eviction, fingerprint-aware invalidation, and metrics hooks so we can observe hit/miss/evict rates in production (`PlanCache`, `IPlanCache`, `RecordingMetrics`).
- Added provider-aware prompt templates so Anthropic and Gemini respond with strict JSON formatting, and tightened the sample JSON guidance delivered to every provider (`LibraryPromptRenderer`).

### Changed

- Split the prompt pipeline into `LibraryPromptPlanner` and `LibraryPromptRenderer`, wiring them through the orchestrator and prompt builder to keep planning deterministic, simplify rendering, and make caching possible.
- Reworked `LibraryAwarePromptBuilder` to guard against token-drift outliers (>30%), invalidate cached plans when drift is detected, and preserve deterministic sampling/ordering while still trimming to budget.
- Internalized the orchestrator wiring inside `BrainarrImportList` to avoid DryIoc recursive-resolution issues and to ensure all planner/renderer dependencies are registered consistently at runtime.

### Fixed

- Corrected release packaging so Brainarr files land directly in Lidarr's plugin folder without creating an extra Brainarr directory.
- Rebuilt the plugin against Lidarr 2.14.2.4786 assemblies as part of the release pipeline (superseded by 1.2.5 after the build still picked up stale assemblies).
- Eliminated `LazyLoaded<T>` access from parallel style aggregation and materialize style context sequentially before parallelizing, removing the race that caused intermittent analyzer failures.
- Stabilized the plugin smoke test workflow by waiting for Lidarr assemblies before executing the sanity build so release pipelines stop flaking.

### Testing / CI

- Added unit suites for the new plan cache (TTL expiration, fingerprint invalidation), planner determinism, renderer provider templates, and drift invalidation paths to keep coverage above the gate.
- Updated CI to consume the new planner/renderer split, including the cache metrics plumbing and deterministic seed scaffolding.

### Documentation

- Refreshed README and provider docs to reflect the 1.2.4 baseline: verified providers (LM Studio, Gemini, Perplexity), updated model identifiers, compatibility messaging, and troubleshooting guidance.

## [1.2.3] - 2025-09-22

### Fixed

- Hardened style-context population to avoid touching `LazyLoaded<T>` inside `Parallel.ForEach`, preventing hangs and intermittent test failures in large libraries.
- Normalized sampling seed hashing so planner outputs remain deterministic when style order changes.

### Testing / CI

- Expanded deterministic sampling coverage, tightened analyzer/orchestrator tests, and ensured the CI matrix uses the shared Lidarr assemblies with the updated smoke wait loop.

## [1.2.2] - 2025-09-21

### Added

- Delivered the model registry pipeline with JSON-backed model metadata, embedded/ETag-aware fallbacks, and UI synchronization so provider/model lists stay current without rebuilding the plugin.
- Introduced style-aware prompting upgrades: strict/relaxed matching, adjacency expansion, expanded coverage metrics, and richer token budgeting utilities.

### Changed

- Balanced discovery sampling with stable ordering for ties, context-aware weighting, and improved prompt compression so recommendations stay reproducible.

### Fixed

- Hardened registry workflows (packaging, Lidarr path resolution), Gemini guardrails, and registry loader references uncovered during integration.

### Testing / CI

- Added large suites covering registry loading, rate-limiting, orchestration, analyzer metrics, provider selection, and tokenizer behaviors while pinning Lidarr Docker digests for deterministic CI.

## [1.2.1] - 2025-09-05

- Last tagged release prior to the registry and planner/renderer overhauls.

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/v1.3.2...main
[1.3.2]: https://github.com/RicherTunes/Brainarr/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/RicherTunes/Brainarr/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/RicherTunes/Brainarr/compare/v1.2.7...v1.3.0
[1.2.7]: https://github.com/RicherTunes/Brainarr/compare/v1.2.6...v1.2.7
[1.2.6]: https://github.com/RicherTunes/Brainarr/compare/v1.2.5...v1.2.6
[1.2.5]: https://github.com/RicherTunes/Brainarr/compare/v1.2.4...v1.2.5
[1.2.4]: https://github.com/RicherTunes/Brainarr/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/RicherTunes/Brainarr/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/RicherTunes/Brainarr/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/RicherTunes/Brainarr/compare/v1.2.0...v1.2.1
