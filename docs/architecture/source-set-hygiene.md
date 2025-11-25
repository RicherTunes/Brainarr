# Source-Set Hygiene Assessment (Brainarr v1.3.1)

Last updated: 2025-09-25

## Objectives

- Catalogue all project-wide warning suppressions and understand the historical rationale.
- Decide which suppressions we can drop immediately, which require follow-up refactors, and where we should invest in analyzer coverage.
- Provide a work plan for tightening compiler/analyzer gates as part of roadmap item **A1**.

## Suppression Inventory

### `Brainarr.Plugin/Brainarr.Plugin.csproj`

| Code | Description | Current Rationale | Proposed Action |
|------|-------------|-------------------|-----------------|
| CS0618 | Use of obsolete members | Lidarr APIs occasionally mark members obsolete before replacements land in stable builds. | Keep (documented); add TODO to remove once Lidarr provides replacements. |
| CS0168 | Variable declared but never used | Catch blocks with logging stubs. | Replace with discard `_` or log exception; plan cleanup and remove suppression. |
| CS1998 | Async method lacks `await` | Compatibility shims for async signatures returning cached data. | Audit methods; convert to synchronous or add `await Task.CompletedTask`; target removal. |
| CS8618 | Non-nullable field uninitialized | DTOs populated by deserializers. | Convert to nullable or add constructors; feasible to remove with targeted refactor. |
| CS8625 | Null literal to non-nullable | JSON converters using null sentinel. | Investigate per call site; may be resolved alongside CS8618 clean-up. |
| CS8603 | Possible null return | Sanitizers returning optional values. | Add `null!` or change signatures; aim to remove once nullable audit completes. |
| CS8604 | Possible null argument | Runtime guard patterns; some should use `ArgumentNullException.ThrowIfNull`. | Replace with guard helpers; plan removal. |
| CS8601 | Possible null assignment | Serialization glue; same plan as CS8618. | Bundle with nullable audit. |
| CS8602 | Dereference of possible null | Historic defensive code; many fixed elsewhere. | Sweep with static analysis; reduce scope. |
| CS8600 | Converting null literal/possible null to non-nullable | Serialization + DI registration. | Address with explicit null checks. |
| CS8619 | Nullability mismatch in interface implementation | Occurs when wrapping Lidarr interfaces that predate nullability. | Keep until Lidarr updates interfaces; document linking issue. |
| CS8622 | Nullability mismatch in parameter type | Same as CS8619. | Keep pending upstream contract changes. |
| CS8629 | Nullable value type may be null | Optional enums parsed from config. | Add `.GetValueOrDefault()` or `??` defaults; candidate for removal. |
| NU1903 | Package vulnerability advisories | Packages supplied by Lidarr runtime; false positives in CI. | Keep until we pin packages independently. |
| MSB3277 | Assembly version conflict | Occurs when using Lidarr-provided DLLs during design-time build. | Keep while we rely on multiple probing paths; revisit after DI cleanup. |

### `Brainarr.Tests/Brainarr.Tests.csproj`

| Code | Description | Current Rationale | Proposed Action |
|------|-------------|-------------------|-----------------|
| CS0618 | Obsolete members | Tests exercising downgrade paths. | Keep for now; add TODO for removal once obsolete APIs gone. |
| CS1998 | Async method lacks `await` | Helper methods using async signature for data-driven tests. | Update helpers; plan removal. |
| MSB3277 | Assembly version conflict | Test project references plugin output + Lidarr assemblies. | Investigate if we can load via `HintPath`; keep temporarily. |

## Recommended Cleanup Sequence

1. **Quick wins (Sprint-ready)**
   - Convert unused exception variables to discard `_`.
   - Replace async stubs without awaits using synchronous methods or `Task.CompletedTask`.
   - Add explicit guard helpers (`ArgumentNullException.ThrowIfNull`) where null-arg warnings fire.
2. **Nullable DTO audit**
   - Introduce constructors or nullable properties for DTOs deserialized from JSON.
   - Update validators/adapters accordingly.
3. **Serialization & provider bindings**
   - Normalize optional enum handling (use `.GetValueOrDefault()` / `??` defaults).
   - Document remaining mismatches tied to Lidarr upstream interfaces (CS8619/CS8622) with issue references.
4. **Tooling updates**
   - Enable `WarningsAsErrors` for nullable context in Release builds once quick wins ship.
   - Add `EnableNETAnalyzers` + `AnalysisLevel` to test project (mirroring plugin) and adopt CA baseline.

## Work Items

- [ ] Sweep catch blocks for unused exception variables and convert to `_`.
- [ ] Refactor async stubs in providers/tests to avoid CS1998.
- [ ] Launch nullable DTO audit; capture findings in `docs/architecture/nullable-strategy.md` (new doc).
- [ ] Coordinate with Lidarr core team regarding obsolete APIs + nullability contracts; track in roadmap item A1.
- [ ] Prototype build with `NoWarn` reduced to {CS0618, CS8619, CS8622, NU1903, MSB3277}; record new warning count.

## Dependencies & Risks

- Requires access to Lidarr assemblies for accurate build validation.
- Some suppressions depend on upstream compiler settings; engaging Lidarr maintainers early reduces rework.
- Removing NU1903 may block builds until we resolve package replacement or suppress via `NoWarn` on specific references.

## Next Checkpoint

Revisit after quick-win tasks to reassess remaining suppressions and decide whether we can enforce `WarningsAsErrors` for Release builds in Brainarr v1.3.0.
