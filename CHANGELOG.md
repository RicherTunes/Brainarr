# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

### Added

- Added wiki hubs for **Start Here**, **Operations Playbook**, **Provider Selector**, and **Documentation Workflow** to guide new users and contributors through first-run, provider choice, and documentation updates.
- Introduced a README "Quick install summary", sample configuration presets, and a new `docs/FAQ.md` for high-signal troubleshooting answers.

### Changed

- Updated the README documentation map, support guidance, and known limitations to highlight the new onboarding flow.
- Expanded the Observability wiki page with a dashboards/alerting appendix referencing the checked-in Grafana starter panels.

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

- Ensured the build resolves Lidarr assemblies from `ext/Lidarr-docker/_output/net6.0` first so Brainarr compiles against 2.14.2+ and no longer triggers `ReflectionTypeLoadException` during Lidarr startup.
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

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/v1.3.0...main
[1.3.0]: https://github.com/RicherTunes/Brainarr/compare/v1.2.7...v1.3.0
[1.2.7]: https://github.com/RicherTunes/Brainarr/compare/v1.2.6...v1.2.7
[1.2.6]: https://github.com/RicherTunes/Brainarr/compare/v1.2.5...v1.2.6
[1.2.5]: https://github.com/RicherTunes/Brainarr/compare/v1.2.4...v1.2.5
[1.2.4]: https://github.com/RicherTunes/Brainarr/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/RicherTunes/Brainarr/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/RicherTunes/Brainarr/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/RicherTunes/Brainarr/compare/v1.2.0...v1.2.1
