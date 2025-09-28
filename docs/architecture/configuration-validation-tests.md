# ConfigurationValidationTests Assessment

Last updated: 2025-09-25 (production validation service introduced)

## Current State

- Legacy file `Brainarr.Tests/Services/Core/ConfigurationValidationTests.cs` (528 LOC) has been replaced by targeted fixtures; this document captures remaining follow-up.
- Historical monolith mixed unit-style assertions against `BrainarrSettingsValidator` with bespoke helper classes; these have now been replaced by production-facing fixtures and `ConfigurationValidationService`.
- Legacy helpers duplicated production validation logic (URL checks, API key length checks, etc.) instead of exercising the actual plugin validators.
- Legacy helper types previously lived in the test assembly; they have been removed in favor of `ConfigurationValidationService` and focused test coverage.

## Reproduction Notes

1. `dotnet build Brainarr.Tests/Brainarr.Tests.csproj -c Debug` completes with 0 warnings (validated on 2025-09-25).
2. `dotnet test Brainarr.Tests/Brainarr.Tests.csproj --no-build` succeeds but takes ~2m40s due to heavy suites.
3. Future production changes should rely on `ConfigurationValidationService`—avoid re-introducing bespoke helpers.

## Root Causes

- **Type shadowing risk**: mitigated by introducing `ConfigurationValidationService` within `NzbDrone.Core.ImportLists.Brainarr.Services.ConfigurationValidation` and deleting the old helper.
- **Duplication / drift**: Helper re-implemented validation rules (URL checks, API key shapes) leading to divergence. Production validators now cover those paths.
- **Overextended test scope**: The prior single class mixed settings, provider configuration, and connection tests, making failures hard to localize.

## Remediation Plan (Phase 0 / T0)

1. **Separate concerns** — DONE via `BrainarrSettingsValidatorTestsV2`, `ProviderConfigurationValidatorTests`, and `ConfigurationValidationServiceTests`.
2. **Eliminate shadowing helper** — DONE by shipping `ConfigurationValidationService` + summary DTO in production.
3. **Extend coverage** — Add missing edge-case fixtures (DTO mapping, failure paths) to keep parity with historical scenarios.
4. **Add regression guard** — Pending: enforce analyzers/tests to fail build when ambiguous validators appear, and enable nullable warnings-as-errors once cleanup completes.
5. **Performance tightening** — Pending: convert lingering async tests without awaits to synchronous variants.

## Deliverables

- New production service: `Services.ConfigurationValidation.ConfigurationValidationService` + `ConfigurationValidationSummary`.
- New test fixtures: `BrainarrSettingsValidatorTestsV2`, `ProviderConfigurationValidatorTests`, `ConfigurationValidationServiceTests`.
- Roadmap item **T0** updated; docs reference this plan (`tasks/brainarr-tech-roadmap.md`).

## Risks & Dependencies

- Breaking changes to validation rules must stay in sync with Lidarr UI expectations; coordinate with front-end on schema updates.
- Removing helper classes exposed gaps; continue to run regression tests/coverage before deleting legacy scenarios.

## Next Steps

- Add dedicated tests for DTO mapping / warning pathways that still rely on composite validation logic.
- Integrate the new fixtures into coverage & mutation reporting as part of T0.
- Document service usage and plan DI registration for orchestrator slices (A0/A2 follow-up).

## Completed

- Introduced `ConfigurationValidationService` + `ConfigurationValidationSummary`, replacing the bespoke helper (2025-09-25).
- Split tests into focused fixtures exercising production validators (2025-09-25).
- Removed legacy `ConfigurationValidationTests.cs` and deleted helper doubles (2025-09-25).
