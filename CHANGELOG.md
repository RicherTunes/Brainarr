# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [1.5.2] - 2026-05-23

### Adversarial-review hardening of the v1.5.1 hot-fix

Bumps `ext/Lidarr.Plugin.Common` from v1.9.2 to **v1.9.3**, which fixes four defects the post-release adversarial review of v1.9.2 surfaced. The original Lidarr-Docker bug fix is unchanged; v1.9.3 just hardens the graceful-degradation path that v1.9.2 added.

- **F1 (HIGH)** — `TokenProtectorFactory` made public. Plugins that internalise Common via ILRepack (which is all of them) can now read `IsDegradedToPlaintext` and `LastDiagnostics` from their startup code and surface a degradation warning to the operator's log.
- **F2 (HIGH)** — `IsDegradedToPlaintext` now reflects the most-recent factory call instead of being sticky-set forever on the first failure.
- **F3 (HIGH)** — Null-protector envelopes use a distinct `lpc:plain:v1:` prefix so audit queries can tell unprotected blobs from real ciphertext at a glance.
- **F6 (MED)** — Exception filter narrowed to I/O families. `CryptographicException` corruption signals from a damaged keychain / key ring now propagate instead of being silently converted to a plaintext fallback.

UX improvement: HelpText on the `AI Provider` and `Model Selection` fields now documents Lidarr's UI quirk where the model dropdown only auto-refreshes when the API Key field changes (not when Provider changes), and tells users either to re-enter the API key OR save+reopen settings to refresh available models.

See [Lidarr.Plugin.Common v1.9.3 changelog](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/CHANGELOG.md#193---2026-05-23) for full root-cause + fix details.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.1...v1.5.2)

## [1.5.1] - 2026-05-23

### Critical fix — Lidarr Docker startup failure

Bumps `ext/Lidarr.Plugin.Common` from v1.9.0 to **v1.9.2** (which adds the **Lidarr-Docker token-protection startup fix**). User-reported symptom: every `set_ApiKey` call in the Brainarr settings UI threw `UnauthorizedAccessException: Access to the path '/app/bin/.config' is denied` — users could not save any LLM provider API key (Z.AI Coding, OpenAI, Anthropic, etc.). Root cause was in Common's `DataProtectionTokenProtector`, which synthesized a relative `.config/...` key-ring path that resolved against `/app/bin` (the read-only Lidarr install dir) when `$HOME` was empty in hotio / linuxserver Docker images. Brainarr code is unchanged for this release; the fix arrives via the submodule bump.

See [Lidarr.Plugin.Common v1.9.2 changelog](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/CHANGELOG.md#192---2026-05-23) for the full root-cause + fix details.

### Operator-visible behavior

- Plugin startup now logs the active token-protection backend + key-ring path. Watch for `Token protection degraded` warnings — they indicate the key store could not initialise and secrets are stored as plaintext (`lpc:ps:v1:bnVsbA:...` envelope in the SQLite settings JSON). Recover by setting `LP_COMMON_KEYS_PATH` to a writable directory (e.g. `/config/.lidarr-keys`) and restarting Lidarr.
- Operators who want hard-failure instead of graceful-degradation can set `LP_COMMON_REQUIRE_PROTECTOR=true`; the plugin will then refuse to load when the key store is unusable.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.0...v1.5.1)

## [1.5.0] - 2026-05-23

### Security

- **BRN-001 — Actually encrypt API keys at rest.** The previous fix encrypted only the in-memory backing field; the public getter still decrypted and returned plaintext, so `JsonSerializer.Serialize` (which Lidarr's `EmbeddedDocumentConverter` invokes) wrote plaintext to the SQLite settings JSON on every save. The migration script wrote ciphertext into the DB once, then Lidarr's first settings save reverted it. **Now:** all 8 `*ApiKey` public properties expose ciphertext (the raw `lpc:ps:v1:...` backing field). Setters accept either plaintext (UI POST) or ciphertext (DB load, UI POST round-tripping unchanged value) — driven by `IStringProtector.IsProtected`. New explicit decryption boundary `BrainarrSettings.GetDecryptedApiKey(AIProvider)` is the single call site that crosses ciphertext → plaintext for outbound API calls. Four consumer files (`AIProviderFactory`, `ProviderRegistry`, `ModelActionHandler`, `RegistryAwareProviderFactoryDecorator`) updated to use it. New `JsonSerialize_EmitsCiphertext_NotPlaintext` round-trip test pins the contract — this is the test BRN-001 needed from day one.

### Auth-failure gate surface

- Bumps `ext/Lidarr.Plugin.Common` from v1.8.0 to **v1.9.0**, which lands the new `AuthFailureGate` surface, `SecureMemory`, `PagedResponseValidator`, and `Conservative rate-limit profile`. Brainarr's BridgeDefaults registration is unchanged (still no-op handlers per the ImportList-only architecture), but consuming code can now use the gate when it makes sense for provider-specific paths.

### Z.AI Coding hardening

- Temperature parity with Z.AI GLM, drop unused `ToolCalling` capability, allow `BRAINARR_ZAI_CODING_USER_AGENT` env override for the Anthropic-Messages endpoint.

### Tests

- 2946 passed / 0 failed / 9 pre-existing skips (full Brainarr suite minus Docker E2E).

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.4.2...v1.5.0)

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
