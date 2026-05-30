# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

### Fixed (tests — 2026-05-30)
- **`LimiterRegistryBounded` full-suite flake eliminated at the root.** The collection name was used by three test classes but had no `[CollectionDefinition]`, so it ran in parallel with collections that mutate `LimiterRegistry`'s process-wide static dictionaries — racing the bounded-dict assertion (passed isolated, flaked ~1/3 full runs). Added `[CollectionDefinition("LimiterRegistryBounded", DisableParallelization = true)]` to serialize all LimiterRegistry-static-state tests (same mechanism `OrchestratorIntegration` uses), which also fixes the latent same-cause flake in `LimiterRegistryMaintenanceTests`. Restored the strong clear-then-insert assertion + added a race-immune bound check. Green across 8 consecutive full-suite runs.

### Added (recommendation engine + style-seeded discovery — 2026-05-29)
- **Discovery-mode escalation on dedup saturation.** During top-up, when iterations stop producing new artists (the library/history dedup keeps rejecting the same cluster) and you're still under target, Brainarr now *widens* the effective discovery mode one step toward Exploratory (Similar→Adjacent→Exploratory) and keeps going, instead of giving up. Live-confirmed: a lo-fi run saturated at iterations 3–4 (0% unique), escalated Adjacent→Exploratory, and iteration 5 broke out with fresh artists — lifting the result from ~25 to **33/50**. Gated to the aggressive/top-up path (where filling the target is the goal), bounded by a hard iteration ceiling, and the original `DiscoveryMode` setting is never persisted-over. (`IterativeRecommendationStrategy.TryEscalateDiscoveryMode`)
- **Search by music style — even styles your library doesn't contain.** Selecting (or free-typing) styles in **Music Styles** now seeds *genre-first* discovery: when your library has zero coverage of the chosen styles, Brainarr recommends the defining artists OF those styles instead of trying (and failing) to tie them to your existing collection. Live-confirmed: with a non-lo-fi library and "lo-fi" selected, it returned Nujabes, J Dilla, MF DOOM, Tomppabeats, Birocratic, Tokimonsta, Joji, Kiefer, … (`LibraryPromptRenderer` genre-first prompt; `RecommendationPipeline.IsStyleSeededDiscovery` skips the library-consistency post-filter in this mode; both gate on the *same* `StyleContext.StyleCoverage==0` signal so they never disagree). The 85-entry catalog dropdown already existed; **freestyle text** (styles not in the catalog, e.g. "vaporwave") now passes through as a seed anchor instead of being silently dropped (`DefaultStyleSelectionService`).
- **`AI Request Timeout` now actually governs the whole run.** The overall recommendation fetch budget is derived from `AIRequestTimeoutSeconds × (1 + top-up iterations) + overhead` (`BrainarrSettings.GetOverallFetchTimeoutMs`), not a hardcoded 120s. Previously a raised timeout (e.g. 360s for slow GLM-5.x reasoning models) was silently guillotined at 2 minutes mid-top-up, capping results. Floored at the legacy 120s, capped at 30min, and it mirrors the local-provider (Ollama/LM Studio) timeout elevation so a single local request is never starved.
- **`max_tokens` scales to the target count** instead of a flat 2000 (`GetOutputTokenBudget`), but is bounded by what the model can generate within the per-request timeout — overshooting just cancels the call mid-stream (nothing to salvage). So a larger list completes in one request when you grant the time, and short timeouts still floor at the proven-safe 2000. (This is the *output/completion* cap — unrelated to the model's much larger input context window.)
- **Run summary reports target attainment** (`items/target` + %) distinctly from the provider-health success rate (now labeled `providerSuccess`), plus an under-target explainer naming the likely cause (timeout / dedup / gating) — ending the "100% success but 17/50 delivered" confusion.

### Added
- `TestValidationBuilder` adopted in `ConfigurationValidator.Validate` (`Brainarr.Plugin/Services/Core/ConfigurationValidator.cs`). Per-provider credential/URL field requirements now gate the behavioral connection probe. **User-visible outcome**: when an API key is empty for a cloud provider, the user sees `OpenAI API key is required. Get yours at https://platform.openai.com/api-keys.` instead of the generic `Unable to connect to AI provider` that the connection probe would have emitted on the failed provider construction. Maps every entry in `AIProviderFactory.CheckProviderAvailability` to its corresponding settings field with a hint pointing at where to obtain the credential. `ClaudeCodeCli` stays N/A (binary auto-detected from PATH). Closes the parity-matrix `TestValidationBuilder MISSING` axis.
- `manifest.json` gains a `commonVersion: "1.16.0"` field that matches `plugin.json`. The new `ManifestJson_MatchesPluginJsonCommonVersion` contract test (`Brainarr.Tests/Contracts/VersionContractTests.cs`) ports apple's regression-guard pattern (`AppleMusicarr.Core.Tests/Contracts/VersionContractTests.cs:59`) — caught apple 3 times in May 2026 (v0.5.5/v0.5.6, v0.5.7, v0.5.8) before the test was added. Closes the parity-matrix `manifest.json lacks commonVersion` gap that the audit flagged.

### Changed
- `Refactor: SecureUrlValidator.ContainsPathTraversal delegates to Common.PathTraversalGuard.ContainsTraversalAttempt (Wave 18G ecosystem parity). Local predicate removed.`
- **`LlmAuthCircuit` refactored to a facade over Common v1.16.0's `AuthFailureGate` + `SlidingWindowAuthFailureHandler`** (`Brainarr.Plugin/Services/Resilience/LlmAuthCircuit.cs`). The brainarr-internal phase state machine (Closed/Open/HalfOpen with custom timers) is replaced by Common's shared gate stack. Public API (`IsOpen` / `RecordAuthFailure` / `RecordSuccess` / `MakeKey`) and documented behavior are unchanged; 24 LlmAuthCircuit tests + provider adoption tests stay green. The SHA-256-hashed key derivation, sliding-window semantics, and 30-min open-duration timer are preserved — key hashing in `MakeKey`, sliding window in `SlidingWindowAuthFailureHandler`, open-duration timer as a brainarr-side `LatchedAt` layer above the gate (Common's `AuthFailureGate.TryAcquireProbeSlot` grants the first probe immediately on first call, so brainarr layers the openDuration wait locally to keep the documented "stay Open for D before any probe" contract). Closes the ecosystem-parity divergence row for AuthFailureGate — all four plugins are now ✓.
- `ext/Lidarr.Plugin.Common` submodule bumped to **v1.17.0** (commit `639d573`) Wave-23 — picks up Wave-21 parity helpers (`PathTraversalGuard.ContainsTraversalAttempt` probe, `AlbumDownloadUri` parser, `AlbumReleaseInfoBuilder` Edition/Explicit/Live bracket slots, unified plugin-version-bump helper). Wave-22 had bumped to v1.16.0 (`936556e`) for `SlidingWindowAuthFailureHandler`; Wave-23 restored ecosystem lockstep after applemusicarr was discovered ahead at v1.17.0 while the others were at v1.16.0. `ext-common-sha.txt`: `f90ecef` → `936556e` (Wave-22) → `639d573` (Wave-23). `plugin.json` + `manifest.json` `commonVersion`: 1.16.0 → 1.17.0.
- CLAUDE.md `## Common helpers in use` section's prior `### Common helpers intentionally not adopted (architectural divergence)` subsection rewritten to document the convergence: `LlmAuthCircuit` is now a thin facade over Common's gate stack rather than an architectural divergence.
- **`LlmAuthCircuit` coverage extended from 3 to all 11 cloud / subscription providers** (Wave-22 Phase D, commit `bad1064`). Newly wired: Perplexity, OpenRouter, DeepSeek, Groq, Gemini, Z.AI GLM, Z.AI Coding, OpenAI Codex Subscription. Subscription providers key the circuit on credentials-file path (closest stable identity since the bearer is loaded per-call from disk). Local providers (Ollama, LM Studio) + CLI provider (ClaudeCodeCli) intentionally skip the circuit. Each provider ctor gains an optional `LlmAuthCircuit? authCircuit = null` parameter that defaults to a fresh per-instance circuit for backwards compat. **User-visible outcome**: a bad API key for ANY of the 11 cloud providers now stops hammering the upstream after 3 consecutive 401/403 in 5 min, instead of only the original 3. 136 affected tests still green.
- **`LlmAuthCircuit` wired into `BrainarrOpenAiCompatibleProvider`** (Wave-23, commit `9fef5d1`) — the 12th auth-bearing provider that Wave-22 missed. Null-safe gating: `_authCircuit` is nullable, only constructed when `_apiKey != null`; self-hosted backends (llama.cpp, vLLM, LocalAI) commonly run without auth and skip the circuit entirely.

### Fixed (security — Wave-23)
- `LlmAuthCircuit.MakeKey` apiKey guard tightened from `IsNullOrEmpty` to `IsNullOrWhiteSpace` (commit `20b133f`). Whitespace-only values (" ", "\t", "\n") would otherwise hash to a single collision-prone slot just like the Wave-22 empty-string case. All known callers pre-validate apiKey, so this is defense-in-depth.
- `GeminiModelDiscovery.CreateCacheKey` — same null-coerce fix; rejects null/empty/whitespace apiKey explicitly instead of producing the constant SHA256("") cache slot. Gemini requires an API key in practice, so this branch shouldn't fire — explicit throw turns a silent collision into a fail-fast configuration error.

### Added (parity — Wave-23)
- `BrainarrConstants` gains the `PluginName` / `ServiceName` / `PluginVendor` triple matching apple/tidal/qobuz convention (commit `88ad013`). Closes parity-matrix row #5 — brainarr was the only plugin without the identity triple in its named constants block. The strings already existed hardcoded in `BrainarrInstalledPlugin` (load-bearing host registration); now there's a single source of truth.

### Tests (Wave-23)
- New `MakeKey_WhitespaceApiKey_ThrowsArgumentException` [Theory] in `Brainarr.Tests/Services/Resilience/LlmAuthCircuitTests.cs` — 5 cases (" ", "   ", "\t", "\n", " \t\n ").

### Changed (cleanup — Wave-23)
- `Brainarr.Tests/TechDebt/DIWiringAndParityTests.cs` → `Brainarr.Tests/DependencyInjection/DIWiringAndParityTests.cs` (commit `8830609`). Wave-22's TechDebt deletion left the parent dir misleading; the test is about DI wiring, not tech-debt remediation. Namespace + Category trait updated.
- Stale `TechDebtRemediation` references purged from `prod_files.txt`, `DELEGATION_PLAN.md`, `tasks/brainarr-tech-roadmap.md`. `docs/TECHDEBT_TEARDOWN_PLAN.md` gains a ✅ status banner explaining it's retained as historical context — the recommended migrations it described are now in place.

### Fixed (Z.AI Coding — 2026-05-29)
- **Z.AI Coding-Plan provider now returns recommendations end-to-end.** Two live-confirmed fixes (Lidarr Docker E2E against a real Coding-Plan key):
  1. **`temperature` dropped from the request body** (`BrainarrZaiCodingProvider.BuildRequestBody`). Z.AI's Anthropic-format Coding endpoint rejects *any* request carrying `temperature` with `[1210][Invalid API parameter]` — Claude Code, which the endpoint emulates, never sends it. This was the root cause of the recurring `[1210]` users saw on every sync/test once auth and model selection were correct. With `temperature` omitted the same request returns `200` + a full completion. `max_tokens` deliberately stays at the host default (2000): a *larger* cap is counter-productive — GLM treats the headroom as licence to pad with reasoning prose and overruns the request timeout (4096/8192 → `TimeoutException`) before closing the JSON array, yielding zero items.
  2. **Truncated-array salvage in `RecommendationJsonParser`.** Verbose models (notably GLM, which wraps output in a ```` ```json ```` fence and pads) routinely hit `max_tokens` mid-array — no closing `]`, so the existing relaxed-parse and first-`[`..last-`]` fallbacks recovered *nothing* even though dozens of complete objects preceded the cut. A container-stack walk (string/escape-aware) extracts each object **whose enclosing container is an array** and parses it independently, discarding only the partial tail. Critically this handles **both** shapes GLM emits interchangeably — a bare root array `[{…},{…}]` *and* an object-wrapped one `{"recommendations":[{…},{…}]}` whose outer `{` never closes when truncated (the wrapped form was silently yielding 0 items on otherwise-successful requests). **Benefits every provider that can hit `max_tokens`, not just GLM.** **User-visible outcome**: ZaiCoding went from `0` to `16–27` validated recommendations per sync at 100% success rate.
- **Reasoning (`thinking`) is intentionally not sent.** Investigated for slow GLM-5.x: Z.AI's endpoint *accepts* Anthropic's `thinking:{type:disabled}` (no `[1210]`) but it has no measurable effect — GLM-5.x latency is raw generation speed (~47 tok/s of plain JSON, not thinking-block padding). Sending an ignored param on a strict endpoint is pure risk, so the request stays minimal/Claude-shaped.
- **Timeout error is now actionable.** GLM-5.x reasoning models need ~45–60s per request, so the default 30s AI timeout fails them outright (a timeout returns nothing to salvage). The timeout message now tells the user to raise **AI Request Timeout** to 60–90s or pick the faster **GLM-4.5-Air** (~10s syncs), instead of a bare "timed out" that reads like a network fault.
- **Misleading per-request log demoted.** The `[ZaiCoding] outbound ...` line is now `Debug` (was a temporary `Info` diagnostic); the shared `LlmLogger` `Model=default` line remains an unset-logging default and does not reflect the real model on the wire — the debug line shows the resolved GLM id for support.
- **Known transient**: the *first* ZaiCoding request after a Lidarr restart can hit the request timeout from cold-start latency (TLS handshake + raw-HttpClient first connection + model warmup). It self-heals on the next sync; raising **AI Request Timeout** in the import-list settings also avoids it.

### Tests (Z.AI Coding — 2026-05-29)
- `CompleteAsync_OmitsTemperature` regression guard — the request body must not contain `temperature`.
- 4 `RecommendationJsonParser` salvage cases: truncated tail (no closing bracket), braces inside string values, nested objects extracted at top level only, and well-formed passthrough (salvage must not alter valid input).

### Dependencies (2026-05-29)
- `ext/Lidarr.Plugin.Common` re-pinned to `24b43c1` — picks up the PathTraversalGuard trailing-separator fix (#552), packaging-gates canonical-abstractions opt-in (#549), and the local-ci .NET 8 runtime guardrail (#548). `ext-common-sha.txt` + submodule gitlink advanced together (594a73b → 24b43c1). Plugin builds clean against the bump; ZaiCoding E2E re-validated (27 recs, 100%).

## [1.5.6] - 2026-05-24

### Changed
- `PluginLogContext` observability wrapping extended to all 9 remaining cloud providers (Wave 15A) — structured per-request correlation now covers every provider.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.5...v1.5.6)

## [1.5.5] - 2026-05-24

### Added
- `PluginLogContext` + `Scrub` observability adopted at 5 entry points — structured per-request correlation and log redaction now cover the full hot path (Wave 13B).

### Changed
- `BrainarrModule.Dispose` wired to `PluginLifecycle.Shutdown` — deterministic teardown ordering on Lidarr plugin unload.
- CLAUDE.md updated: Common helpers reference table extended; quarantined-test list corrected to 3 actual tests with revival notes.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.4...v1.5.5)

## [1.5.4] - 2026-05-24

### Added
- `LlmAuthCircuit` — per-(provider, api-key) auth-failure breaker for cloud providers (OpenAI, Anthropic, ClaudeCodeSub); stops hammering a provider with a bad key until the circuit resets.

### Fixed
- `MetricsCollector` + `LimiterRegistry` dictionaries bounded via `BoundedConcurrentDictionary`; timers disposed on module unload — eliminates unbounded memory growth in long-running Lidarr instances.
- `MetricsCollector` tests de-flaked by sharing xUnit collection (eliminates timer-race false positives).

### Changed
- Module teardown migrated to `PluginLifecycle.Shutdown` — consistent shutdown ordering across the plugin ecosystem.
- `HostGateRegistry.Shutdown` called on module dispose — releases the gate timer on Lidarr plugin unload.

### Dependencies
- Common submodule bumped to v1.10.0.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.3...v1.5.4)

## [1.5.3] - 2026-05-23

### Fixed
- Replace hand-rolled `SpecialFolder.ApplicationData` path chains with `PluginConfigRoots` — eliminates the Docker/hotio `/app/bin/.config` write failure for tokenizer and file stores.

### Changed
- `BackendHealthCache` extended to completion path; `ReviewQueueService` storage migrated to Common's `JsonFileStore<TKey,TValue>` (removes ad-hoc JSON serialization).
- `WarnOnce` log-gating helper adopted from Common — eliminates static `HashSet` guards in hot paths.

### UX
- Model Selection `HelpText` clarified to explain the Lidarr UI refresh limitation (model list doesn't update until settings modal is reopened).

### Dependencies
- Common submodule bumped to v1.9.5.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.2...v1.5.3)

## [1.4.0] - 2026-05-23

### Phase 0 + Phase 1 — Ecosystem Alignment

#### Ecosystem version contract (Phase 0.3)

- Bumped `commonVersion` to `1.8.0` in `plugin.json` and `manifest.json` to align with Common v1.8.0.
- Dropped `net6.0` from CI matrix; plugin targets `net8.0` only per the ecosystem version contract.
- Fixed manifest drift: removed forbidden `minimumVersion` field from `manifest.json`; `plugin.json` and `manifest.json` version fields now match exactly.
- Parity-lint `VersionContract` check passes (`ecosystem-parity-lint.ps1 -Check VersionContract`).

#### Phase 0 — manifest hygiene

- `plugin.json` and `manifest.json` aligned on `id`, `version`, `apiVersion`, `targetFramework`, and `rootNamespace` per `parity-spec.json`.
- Bridge-exempt governance fields added to `.bridge-exempt`; review cadence documented.
- Common submodule bumped to v1.8.0 (from v1.7.1).

#### Phase 1 — docs and security

- Security hardening backlog added: 10 findings, 2 High severity — see `docs/SECURITY_HARDENING_BACKLOG.md`.
- README augmented with Shared Infrastructure section (Common services consumed, version contract reference).
- Documentation section added to README with links to CHANGELOG, CONTRIBUTING, SECURITY, and docs/.
- `docs/archive/` already contained historical audit and refactoring reports from earlier waves.

### Added (wiki / docs — carry-forward from prior unreleased)

- Wiki hubs for **Start Here**, **Operations Playbook**, **Provider Selector**, and **Documentation Workflow**.
- README "Quick install summary", sample configuration presets, and `docs/FAQ.md`.

### Changed (carry-forward from prior unreleased)

- README documentation map, support guidance, and known limitations updated to highlight new onboarding flow.
- Observability wiki page expanded with dashboards/alerting appendix referencing the checked-in Grafana starter panels.

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

### Changed
- CI: Added `scripts/ci/check-assemblies.sh` and wired it into core workflows to fail fast when required Lidarr assemblies are missing or from the wrong source/tag.
- CI: Bumped `LIDARR_DOCKER_VERSION` to `pr-plugins-2.14.2.4786` across workflows (including nightly perf and dependency update).
- CI: Dependency update workflow now uses Docker-based assembly extraction, adds a concurrency group to avoid overlaps, and verifies assemblies with the sanity script.

### Documentation
- README: align badges/version lines and add local CI one-liners.
- Provider matrix/docs: bump headers/status strings to v1.3.1.
### Changed

- CI: Added scripts/ci/check-assemblies.sh and wired it into core workflows to fail fast when required Lidarr assemblies are missing or from the wrong source/tag.
- CI: Bumped LIDARR_DOCKER_VERSION to pr-plugins-2.14.2.4786 everywhere (including nightly perf and dependency update jobs) to keep in sync with the plugins branch.
- CI: Dependency update job now uses Docker-based assembly extraction (ext/Lidarr-docker/_output/net8.0), adds a concurrency group to avoid overlapping runs, and verifies assemblies via the new sanity script.

### Documentation

- README version badge and “Latest release” references updated to v1.3.1.

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
