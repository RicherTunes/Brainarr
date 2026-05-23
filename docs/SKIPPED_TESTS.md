# Skipped Tests Registry

> Phase 1 audit — branch `cleanup/delete-dead-enhanced-rate-limiter`.
> Tests discovered via `grep -rn 'Skip = ' Brainarr.Tests/`.

## Summary

| # | Test | File:Line | Reason | Owner | Removal Condition |
|---|------|-----------|--------|-------|-------------------|
| 1 | `Fetch_WithValidSettings_CallsOrchestrator` | `BrainarrImportListIntegrationTests.cs:140` | Requires Lidarr host `Settings` injection — `ImportListBase.Settings` is populated by the Lidarr host at runtime and cannot be set via constructor in unit tests. | RicherTunes | Mock `ImportListBase.Settings` via reflection or a test-shim subclass, OR promote to E2E test against a live Lidarr host container. |
| 2 | `Fetch_WhenOrchestratorReturnsEmpty_ReturnsEmptyList` | `BrainarrImportListIntegrationTests.cs:164` | Same as #1 — `Settings` injection from Lidarr host. | RicherTunes | Same as #1. |
| 3 | `Fetch_WhenOrchestratorThrows_PropagatesException` | `BrainarrImportListIntegrationTests.cs:182` | Same as #1 — `Settings` injection from Lidarr host. | RicherTunes | Same as #1. |
| 4 | `Test_WithValidConfiguration_DoesNotAddFailures` | `BrainarrImportListIntegrationTests.cs:202` | Same as #1 — `Settings` injection from Lidarr host, called from `TestConfiguration`. | RicherTunes | Same as #1. |
| 5 | `Test_WithInvalidConfiguration_AddsFailures` | `BrainarrImportListIntegrationTests.cs:224` | Same as #1 — `Settings` injection from Lidarr host, called from `TestConfiguration`. | RicherTunes | Same as #1. |
| 6 | `Write_emits_expected_camel_case_strings` | `Configuration/StopSensitivityJsonConverterDirectTests.cs:34` | **RESOLVED — unskipped in Phase 1.** The original skip was because STJ `Utf8JsonWriter` throws `InvalidOperationException` when used as a root-level writer without a surrounding JSON structure. Fix: wrap the enum value in a `Wrapper` object and call `JsonSerializer.Serialize`, which is what the test now does. | RicherTunes | Already unskipped. |
| 7 | `PackagingFactAttribute` conditional skip | `Packaging/PackagingFactAttribute.cs:18` | Runtime conditional: skips when the plugin `.zip` is absent (i.e., no `./build.ps1 -Package` has been run). This is expected behaviour in developer and PR contexts. | RicherTunes | Set `REQUIRE_PACKAGE_TESTS=true` in CI or run `./build.ps1 -Package` locally before running packaging tests. |

## Notes

- Tests #1–#5 share a single root cause: `ImportListBase.Settings` is a Lidarr host-injected property with no setter accessible in unit test scope. The recommended fix for a future phase is to introduce a `TestableImportList` subclass (or use reflection) to seed the `Settings` property, removing the need for a live Lidarr host.
- Test #6 (`Write_emits_expected_camel_case_strings`) has been unskipped as part of this phase. The fix was already present in the test file (wrapper object pattern); the skip reason was stale.
- Test #7 (`PackagingFactAttribute`) is a conditional rather than a permanent skip. Its behaviour is correct; no action required.
