# Shared Library Integration Plan (lidarr.plugin.common)

Last updated: 2025-09-25

## Goals

- Reduce duplicated infrastructure code across Arr plugins we own by upstreaming common services into the `lidarr.plugin.common` submodule.
- Establish a repeatable workflow for proposing changes, validating compatibility, and consuming updates without destabilising Brainarr releases.
- Prepare the path toward a future NuGet distribution while the submodule remains the source of truth.

## Current State Snapshot

- Submodule location: `ext/lidarr.plugin.common` (mirrors GitHub repo).
- Key reusable code in Brainarr today:
  - Security: `SecureHttpClient`, `SecureApiKeyManager`, `SecureApiKeyStorage`, `PromptSanitizer`, `SecureUrlValidator`.
  - Resilience: `LimiterRegistry`, `BreakerRegistry`, `CircuitBreaker`, `RetryPolicy`, `TimeoutContext`.
  - Support utilities: `SafeAsyncHelper`, `TokenCostEstimator`, telemetry primitives, `ShuffleUtil`.
- Brainarr references the submodule via a direct project reference (see `Brainarr.Plugin/Brainarr.Plugin.csproj`) so builds consume the real shared code.

## Integration Guiding Principles

1. **Single source for primitives**: migrate classes once they stabilise, then consume from the submodule to avoid drift.
2. **Backwards compatibility**: provide shims/adapters in Brainarr while refactors propagate to sibling plugins.
3. **Incremental extraction**: move one cohesive slice per PR (e.g., security helpers), accompanied by unit tests and documentation updates.
4. **Version pinning**: update submodule SHA explicitly per feature branch; never auto-update on `main` without validation.
5. **Testing parity**: ensure Brainarr + lidarr.plugin.common share test coverage for migrated components (transfer tests or add new ones).

## Proposed Workstreams

### Wave 0 – Preparation

- [ ] Add documentation in `lidarr.plugin.common` describing contribution workflow (draft PR in shared repo).
- [x] Add Brainarr bootstrap sanity check ensuring `Lidarr.Plugin.Common.dll` is packaged (build.ps1 update on 2025-09-27).
- [ ] Introduce solution-level references (or script) allowing Brainarr tests to run against submodule projects locally.
- [ ] Define semantic version scheme for submodule tags even before NuGet packaging (e.g., `v0.x` pre-release).

### Wave 1 – Security Utilities (Roadmap S1)

- Move `SecureHttpClient`, `SecureUrlValidator`, `PromptSanitizer`, `InputSanitizer`, and API key helpers into the submodule.
- Provide wrapper classes in Brainarr that reference the shared implementations to maintain namespace stability.
- Update documentation (README, SECURITY.md) to reference shared sources.
- Add unit tests in lidarr.plugin.common replicating Brainarr coverage; adjust Brainarr tests to consume the shared implementations.

### Wave 2 – Resilience & Async Infrastructure (Roadmap S2)

- Extract `LimiterRegistry`, `BreakerRegistry`, `CircuitBreaker`, `RetryPolicy`, and `SafeAsyncHelper`.
- Align interfaces (`ILimiterRegistry`, etc.) across Brainarr and shared repo; introduce DI registration helpers in the submodule.
- Benchmark both repos after extraction to ensure no regressions.

### Wave 3 – Telemetry & Token Costing (Roadmap S3)

- Upstream `ProviderMetricsHelper`, `MetricsCollector`, and `TokenCostEstimator`.
- Create shared telemetry abstractions (interfaces, DTOs) to feed Arr-wide observability initiatives.
- Document expected metrics output contract in the shared repo.

## Workflow Checklist

1. Raise tracking issue in `lidarr.plugin.common` for each wave.
2. Build submodule locally (`dotnet build`) to ensure compatibility.
3. Port code + tests; commit in submodule repo.
4. Update Brainarr to point submodule to new commit; adjust project references to use shared classes.
5. Run Brainarr test suites (unit + integration) ensuring parity.
6. Update roadmap progress (S1/S2/S3) and docs accordingly.
7. Coordinate rollout with sibling plugins (e.g., Readarr) to avoid diverging abstractions.

## Risks & Mitigations

- **Submodule drift**: enforce PR templates requiring consumer validation before merging.
- **Breaking changes across plugins**: version the submodule and maintain change log; require dependent PRs to upgrade deliberately.
- **Testing complexity**: create shared test helper libraries to avoid replicating fixtures between repos.

## Next Actions (C0)

- Schedule alignment meeting with shared repo maintainers; record decisions in this document.
- Identify owners for Wave 1 extraction and open preparation issues (docs + build pipeline).
- Evaluate current submodule build scripts to support local testing (`dotnet test`) without manual path tweaks.
