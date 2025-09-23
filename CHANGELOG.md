# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

- No entries yet.

## [1.2.4] - 2025-09-23

### Added
- Introduced a dedicated prompt plan cache with TTL, LRU eviction, fingerprint-aware invalidation, and metrics hooks so we can observe hit/miss/evict rates in production (`PlanCache`, `IPlanCache`, `RecordingMetrics`).
- Added provider-aware prompt templates so Anthropic and Gemini respond with strict JSON formatting, and tightened the sample JSON guidance delivered to every provider (`LibraryPromptRenderer`).

### Changed
- Split the prompt pipeline into `LibraryPromptPlanner` and `LibraryPromptRenderer`, wiring them through the orchestrator and prompt builder to keep planning deterministic, simplify rendering, and make caching possible.
- Reworked `LibraryAwarePromptBuilder` to guard against token-drift outliers (>30%), invalidate cached plans when drift is detected, and preserve deterministic sampling/ordering while still trimming to budget.
- Internalized the orchestrator wiring inside `BrainarrImportList` to avoid DryIoc recursive-resolution issues and to ensure all planner/renderer dependencies are registered consistently at runtime.

### Fixed
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

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/v1.2.4...main
[1.2.4]: https://github.com/RicherTunes/Brainarr/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/RicherTunes/Brainarr/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/RicherTunes/Brainarr/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/RicherTunes/Brainarr/compare/v1.2.0...v1.2.1
