# TechDebt Teardown Plan

> ✅ **Status (Wave-22 / 2026-05-25):** The entire `Brainarr.Plugin/TechDebt/`
> directory was deleted in commit `7e7a722`. This document is retained for
> historical context — describes the analysis that informed the deletion.
> The migration paths recommended below (SafeAsyncHelper, structured error
> handling, LogRedactor for sensitive-data scrubbing) are now in place.

> Phase 1 analysis — branch `cleanup/delete-dead-enhanced-rate-limiter`.
> No code changes in this pass. Phase 1.2 will implement migrations.

## Source: `TechDebtRemediation.cs`

`Brainarr.Plugin/TechDebt/TechDebtRemediation.cs` defines `ITechDebtRemediation` (interface) and `TechDebtRemediationService` (implementation), plus the `TechDebtExtensions` static helper class.

The three public services provided are:

1. **`SafeExecuteSync<T>` / `SafeExecuteSync`** — async-to-sync bridge (delegates to brainarr's own `SafeAsyncHelper.RunSafeSync`).
2. **`ExecuteWithStandardErrorHandling<T>`** — structured try/catch over an async op, logging `TaskCanceledException`, `TimeoutException`, `HttpException`, and generic `Exception`, returning a default value on failure.
3. **`StandardizeResponseParsing`** — provider-agnostic JSON/text parsing of AI responses into `List<Recommendation>`, with normalization.

---

## Method-by-method analysis

### 1. `SafeExecuteSync<T>` / `SafeExecuteSync`

| Attribute | Detail |
|-----------|--------|
| What it does | Wraps `SafeAsyncHelper.RunSafeSync` — a thread-pool-based async-to-sync bridge that avoids deadlocks caused by `.GetAwaiter().GetResult()` on synchronisation-context threads. |
| Common equivalent? | `Lidarr.Plugin.Common.Services.SafeOperationExecutor` exists but does **not** provide an async-to-sync bridge. It provides `ExecuteWithTimeoutAsync`, `TryExecuteAsync`, and `ExecuteWithFallbacks` — all of which are fully async. There is **no** `RunSafeSync`-equivalent in Common. |
| Recommendation | **Keep in brainarr, rename for clarity.** `TechDebtRemediationService.SafeExecuteSync` is a thin pass-through to `SafeAsyncHelper.RunSafeSync`; the value is in `SafeAsyncHelper` itself (which is already in `Brainarr.Plugin/Utils/`). Remove the pass-through from `TechDebtRemediationService` and use `SafeAsyncHelper.RunSafeSync` directly at call sites. The extension methods `SafeGetResult<T>` and `SafeWait` in `TechDebtExtensions` may be removed once all call sites are updated. |
| Candidate Common type | None today. If Common needs this in future, propose `Lidarr.Plugin.Common.Services.SyncBridgeExecutor` wrapping the same TaskFactory pattern. |

### 2. `ExecuteWithStandardErrorHandling<T>`

| Attribute | Detail |
|-----------|--------|
| What it does | Async try/catch with structured logging for `TaskCanceledException`, `TimeoutException`, `HttpException`, and `Exception`; returns `defaultValue` on any failure. |
| Common equivalent? | `Lidarr.Plugin.Common.Services.SafeOperationExecutor.ExecuteWithTimeoutAsync` is similar but: (a) does not log, (b) requires a `TimeSpan` timeout, (c) does not discriminate HTTP exceptions. `Lidarr.Plugin.Common.Utilities.GenericResilienceExecutor.ExecuteWithResilienceAsync` covers retry/rate-limit logic but is HTTP-transport-specific and much more complex. `Lidarr.Plugin.Common.CLI.Commands.BaseCommand.ExecuteWithErrorHandlingAsync` is internal to the CLI subsystem and not applicable here. |
| Recommendation | **Propose migration to Common as `SafeOperationExecutor.ExecuteWithErrorHandlingAsync<T>`** (new overload, no mandatory timeout, structured logging via `NLog.Logger`). Until Common accepts the contribution, **keep the method in brainarr** but move it out of `TechDebt/` into `Utils/` or a new `Services/Core/ErrorHandling/` namespace. |
| Candidate Common type | `Lidarr.Plugin.Common.Services.SafeOperationExecutor` (new overload). |

### 3. `StandardizeResponseParsing`

| Attribute | Detail |
|-----------|--------|
| What it does | Parses an AI provider response string (JSON array, JSON object, or plain-text `Artist - Album (Year)` lines) into `List<Recommendation>`, then normalises each entry (trim, collapse whitespace, strip `**` markdown, set `Source`/`Provider`/`Confidence`). |
| Common equivalent? | No equivalent exists in Common. This is brainarr-domain logic (knowledge of `Recommendation`, `AIProvider`, brainarr normalisation rules). |
| Recommendation | **Keep in brainarr, move out of `TechDebt/`** into a purpose-built `Services/Core/ResponseParsing/` namespace. Rename to `BrainarrResponseParser` or similar. There is no Common home for this logic because it depends on `Recommendation` and `AIProvider`, which are brainarr-specific types. |
| Candidate Common type | N/A — remains brainarr-internal. |

---

## Removal target for `TechDebt/TechDebtRemediation.cs`

| Phase | Action |
|-------|--------|
| Phase 1.2 | Migrate `ExecuteWithStandardErrorHandling<T>` to `Brainarr.Plugin/Utils/` or `Services/Core/ErrorHandling/`. Update all call sites. |
| Phase 1.2 | Remove `TechDebtExtensions.SafeGetResult<T>` and `SafeWait` — call `SafeAsyncHelper.RunSafeSync` directly at call sites. |
| Phase 1.3 | Move `StandardizeResponseParsing` to `Services/Core/ResponseParsing/BrainarrResponseParser.cs`. |
| Phase 1.3 | Delete `TechDebt/TechDebtRemediation.cs` once all three services have moved. Confirm with `dotnet build` and full test run. |
| v2.0 | After Common accepts `SafeOperationExecutor.ExecuteWithErrorHandlingAsync<T>`, swap brainarr's copy for the Common type and remove the local one. |

---

## Common grep evidence

```
# SafeAsync / RunSafeSync — not in Common:
grep -rn "SafeAsync\|RunSafeSync\|ResilientExecutor" ext/Lidarr.Plugin.Common/src/  # (no results)

# ExecuteWithErrorHandling — only in CLI subsystem, not general:
grep -rn "ExecuteWithErrorHandling" ext/Lidarr.Plugin.Common/src/
# => ext/Lidarr.Plugin.Common/src/CLI/Commands/BaseCommand.cs (CLI-only, internal)

# GenericResilienceExecutor — HTTP-transport-specific:
# ext/Lidarr.Plugin.Common/src/Utilities/GenericResilienceExecutor.cs
# => ExecuteWithResilienceAsync (requires HTTP status-code callbacks, not applicable)
```
