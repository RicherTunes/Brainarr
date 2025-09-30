# Advanced Settings

> Advanced options appear under **Import Lists → Brainarr → Advanced** (and the hidden section). Default values live in `Brainarr.Plugin/BrainarrSettings.cs`; unit tests in `Brainarr.Tests/Services/Prompting` pin expected behaviour. Treat those as the canonical source when adjusting settings or updating docs.

## Quick reference map

| Category | What it controls | Where to read more |
|----------|------------------|--------------------|
| Prompt budgets & compression | Token headroom guard, fallback trimming, prompt compression state | [README ▸ What’s new in 1.3.0](https://github.com/RicherTunes/Brainarr/blob/main/README.md#whats-new-in-130); `Brainarr.Plugin/Services/Prompting/LibraryAwarePromptBuilder.cs`; tests in `TokenBudgetGuardTests` |
| Sampling shape & discovery | Ratios for similar/adjacent/exploratory, album caps, relaxed expansion | `BrainarrSettings.cs` (`SamplingShape`), planner tests (`LibraryPromptPlannerTests`), notes in [`docs/PROVIDER_GUIDE.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/PROVIDER_GUIDE.md) |
| Safety gates & review queue | Minimum confidence, MBID enforcement, Queue Borderline Items | [Review Queue](Review-Queue), [`docs/TROUBLESHOOTING.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/TROUBLESHOOTING.md) |
| Concurrency & throttling | Provider concurrency caps, adaptive throttling, cooldowns | [Observability & Metrics](Observability-and-Metrics), limiter tests in `PlanCacheTests` |
| Deterministic planning & caching | Stable seed generation, plan cache capacity/TTL, fingerprint invalidation | [README ▸ Planner determinism & caching](https://github.com/RicherTunes/Brainarr/blob/main/README.md#planner-determinism--caching), `CacheSettings.cs`, `PlanCacheTests` |
| Provider fallbacks | Priority lists, failover thresholds | [Cloud Providers ▸ Multi-Provider Strategy](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers#multi-provider-strategy) |

## Operating guidelines

1. **Keep defaults in sync.** When changing an advanced option (code or UI), update `BrainarrSettings.cs` and corresponding tests first, then refresh this page with pointers rather than duplicating raw values.
2. **Document rationale.** Record notable overrides (e.g., custom `sampling_shape`, adaptive throttling tweaks) in [`docs/VERIFICATION-RESULTS.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/VERIFICATION-RESULTS.md) alongside the release/verification notes.
3. **Validate with the Review Queue.** If you tighten Safety Gates or enable Guarantee Exact Target, verify behaviour via the Review Queue workflow before rolling to production.
4. **Monitor drift.** Use the Observability preview or Prometheus endpoint (`metrics/prometheus`) to make sure new limits keep latency and 429 rates within expected bounds.

Need field-by-field help? Open Brainarr in Lidarr, hover the info icons in the Advanced tab, and cross-check against the code/test references above. This keeps the UI hints, wiki, and implementation aligned without maintaining duplicate tables.
