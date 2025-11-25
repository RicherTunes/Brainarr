# Brainarr Tech & Architecture Roadmap

Last updated: 2025-09-27

**Target release:** Brainarr v1.3.1

## Context & Goals

- Reduce architectural complexity around orchestration/prompting layers so the plugin is maintainable while it keeps expanding provider coverage.
- Eliminate duplicated logic across provider implementations and pipeline services to enable faster feature delivery and easier bug fixes.
- Mature our testing strategy so regressions are caught before shipping and we can ship faster with confidence.
- Identify code that should live in the shared `lidarr.plugin.common` repo to prevent drift between plugins we own.
- Keep runtime performance, memory usage, and rate limiting predictable as the recommendation workload scales up.

## Guiding Principles

1. Favor composable services over god objects (target <300 LOC per class in orchestration/prompting layers).
2. Prefer extracting reusable primitives into `lidarr.plugin.common` (submodule) once stabilized in Brainarr.
3. Ratchet quality gauges upward (linting, analyzers, tests) instead of allowing indefinite suppression.
4. Instrument critical paths (cache, provider invocations) so we can reason about latency and errors quantitatively.
5. Maintain incremental delivery: ship small slices (feature flags if needed) while migrating architecture.

## Phased Roadmap

### Phase 0 - Immediate (Week of 2025-09-29)

- **A0. Publish orchestrator decomposition blueprint**: capture desired boundaries for `BrainarrOrchestrator`, `LibraryAnalyzer`, `LibraryPromptPlanner`, `LibraryAwarePromptBuilder`, defining responsibilities, dependency graph, and DI wiring changes. _Draft lives in `docs/architecture/brainarr-orchestrator-blueprint.md`.
- **A1. Source-set hygiene**: lock down generated folders (`obj`, `bin`) in repo tooling, trim the `NoWarn` list (see `docs/architecture/source-set-hygiene.md`), enable nullable warnings as errors for Release, and add CA analyzers baseline.
- **T0. Test visibility baseline**: produce coverage + mutation score snapshot in CI, document current flakiness, and unblock `ConfigurationValidationTests` refactor (see `docs/architecture/configuration-validation-tests.md`).
- **C0. Shared library intake**: inventory candidates (`SecureHttpClient`, `SecureApiKeyManager`, `PromptSanitizer`, `RetryPolicy`, `SafeAsyncHelper`, `TokenCostEstimator`, `LimiterRegistry`) and draft migration approach with `lidarr.plugin.common` maintainers.

### Phase 1 - Short Term (Next 2-4 weeks)

- **A2. Orchestrator break-up v1**: extract provider coordination into `RecommendationCoordinator`, move cache + limiter registries behind interfaces, and slim `BrainarrOrchestrator` to workflow composition.
- **A3. Library analysis modularity**: split `LibraryAnalyzer` into pluggable analyzers (e.g., catalog ingestion, dedupe detection, top-up heuristics) with strategy registry.
- **P1. Provider response core**: move `ParseSingleRecommendation` workflow + schema validation into a shared `RecommendationParsingService`, update providers to rely on it, and add integration tests for each provider's unique fields.
- **T1. Fast smoke path**: add deterministic smoke tests executing the happy path for each provider using sanitized fixtures + verifying dedupe, rate-limit, and sanitization behaviors.
- **S1. Shared library extraction wave 1**: upstream security utilities (`SecureHttpClient`, `SecureUrlValidator`, `InputSanitizer`) + safe async helpers into `lidarr.plugin.common`, consume via existing git submodule (evaluate NuGet packaging later).
- **Q1. Build guardrails**: enforce analyzers + StyleCop (or EditorConfig) in CI, fail build when new warnings introduced, and wire `dotnet format --verify-no-changes`.

### Phase 2 - Medium Term (4-8 weeks)

- **A4. Prompting pipeline refactor**: convert `LibraryPromptPlanner`/`LibraryAwarePromptBuilder` into pipelined stages (context gathering, plan generation, template rendering) with caching at stage boundaries.
- **P2. Provider capability registry**: centralize capability discovery (`GeminiModelDiscovery`, `ProviderCapabilities`) into a provider metadata registry stored in shared submodule (future package).
- **P3. Rate limiting unification**: replace bespoke rate limiting implementations with shared `LimiterRegistry` abstractions, add adaptive limiter for high-volume providers, and export metrics.
- **T2. Mutation + perf suites**: integrate Stryker for mutation testing on critical services, add BenchmarkDotNet suites for cache + provider pipelines, and wire optional nightly CI leg.
- **S2. Shared library extraction wave 2**: move resilience components (`CircuitBreaker`, `RetryPolicy`, `LimiterRegistry`, `SecureJsonSerializer`) and token costing into `lidarr.plugin.common` with backwards compatibility shims.
- **O1. Observability**: add structured logging + metrics (histograms for latency, counters for provider errors) surfaced through Lidarr's telemetry.

### Phase 3 - Long Term (8+ weeks)

- **A5. Provider plug-inization**: evaluate splitting providers into discrete assemblies with manifest-driven discovery for selective deployment.
- **A6. Recommendation pipeline DSL**: define declarative pipeline configuration (YAML/JSON) enabling plug-and-play reorder of sanitization, validation, enrichment steps.
- **T3. End-to-end regression env**: provision dockerized integration env running Lidarr + Brainarr nightly with golden datasets and synthetic provider mocks.
- **S3. Shared library GA**: finalize versioned contract with `lidarr.plugin.common`, move Brainarr + sibling plugins to consume stable submodule (or future package) releases, and automate release notes sync.
- **O2. Performance budget automation**: set SLA thresholds (p95 < 2.5s per recommendation batch, memory < 250MB) and enforce via CI regression tests + alerts.

## Workstream Tracker (live)

| ID | Theme | Status | Owner | Notes |
|----|-------|--------|-------|-------|
| A0 | Orchestrator decomposition blueprint | In Progress | Architecture | Draft blueprint published at `docs/architecture/brainarr-orchestrator-blueprint.md`; collect feedback and finalize scope for Slice 1. |
| A1 | Source-set hygiene | In Progress | Platform | Suppression inventory published in `docs/architecture/source-set-hygiene.md`; next up: quick-win cleanup + analyzer baseline. |
| T0 | Test visibility baseline | In Progress | QA | Diagnostic written in `docs/architecture/configuration-validation-tests.md`; refactor plan prepped before enabling coverage jobs. |
| C0 | Shared library intake | In Progress | Platform | Git submodule wired; packaging now copies updated manifest and bundles Lidarr.Plugin.Common + Microsoft.Extensions 7.x stack. Next: sync versioning plan with shared maintainers and extend smoke coverage. |
| A2 | Orchestrator break-up v1 | Blocked | Architecture | Waiting on A0 blueprint approval. |
| P1 | Provider response core | Planned | Providers | Use TechDebtRemediation utility as starting point. |
| S1 | Shared library extraction wave 1 | Planned | Platform | Needs C0 intake decisions and packaging strategy. |

## Next Actions

- Review A0 blueprint draft (`docs/architecture/brainarr-orchestrator-blueprint.md`) with stakeholders and capture decisions by 2025-09-30.
- Execute A1 quick wins: convert unused exception variables to `_`, audit async stubs for `CS1998`, and trial reduced `NoWarn` set.
- Begin T0 refactor by splitting configuration validation tests per `docs/architecture/configuration-validation-tests.md`.
- Smoke-test the refreshed package (bundling `Lidarr.Plugin.Common` + Microsoft.Extensions 7.x runtime) inside Lidarr and capture runtime logs confirming no missing DLLs.

## Risks & Mitigations

- **Scope creep in refactors**: enforce feature flags + small PRs per refactor slice; ensure tests cover acceptance criteria before merging.
- **Shared library churn**: agree on versioning/compatibility policy with `lidarr.plugin.common` to prevent cascading breaking changes.
- **Test flakiness**: prioritize deterministic fixtures and rate limiter resets in the new smoke suites.
- **Resource constraints**: if team capacity drops, focus on Phase 0/1 items and defer long-term experiments.

## References

- `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs` (1426 LOC)
- `Brainarr.Plugin/Services/Core/LibraryAnalyzer.cs` (1412 LOC)
- `Brainarr.Plugin/Services/Prompting/LibraryPromptPlanner.cs` (967 LOC)
- `Brainarr.Plugin/Services/Providers/*Provider.cs` (20-30 KLOC each)
- `Brainarr.Plugin/Services/Caching/EnhancedRecommendationCache.cs` (feature-flagged LRU/weak cache stack)
