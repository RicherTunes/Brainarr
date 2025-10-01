# Planner & cache

The planner turns library samples into deterministic prompts and keeps recent plans warm for fast re-runs. This page explains how the pieces fit together and what the cache guarantees.

## Planner pipeline

1. **Profile & fingerprint** — `LibraryPromptPlanner` builds a library profile from the sampled artists/albums and computes a SHA-256 fingerprint. The fingerprint anchors cache keys and invalidation.
2. **Style selection** — `DefaultStyleSelectionService` chooses style anchors from the sampled library using stable ordering so runs remain reproducible.
3. **Sampling** — `DefaultSamplingService` balances artist-level and global album picks according to the `SamplingStrategy` and the token budget provided by the token policy.
4. **Compression** — `DefaultCompressionPolicy` trims low-signal items, enforces headroom, and records the pre/post token counts for metrics.
5. **Rendering** — `LibraryPromptRenderer` produces the final prompt with either rich or minimal formatting depending on `PreferMinimalPromptFormatting`.

The planner emits `Plan cache configured (version={PlannerBuild.ConfigVersion})` whenever cache settings change. The current build salt lives in `LibraryPromptPlanner.PlannerBuild.ConfigVersion` and should bump when cache key composition or rendering semantics change.

## Cache behaviour

- **Implementation** — `PlanCache` is an in-memory LRU keyed by the planner''s cache key and backed by a fingerprint index.
- **Defaults** — Capacity 256, TTL five minutes. Both are normalised (`MinCapacity = 16`, `MaxCapacity = 1024`, `MinTtl = 1`, `MaxTtl = 60`).
- **Sliding TTL** — Cache hits refresh the expiry window (`PlanCache.TryGet` rewrites the entry with `now + ttl`).
- **Sweep** — A background sweep runs every 10 minutes to remove expired entries even without lookups.
- **Invalidation** — `InvalidateByFingerprint` drops all entries associated with a library fingerprint whenever the fingerprint changes or the planner trims for headroom.
- **Metrics** — `prompt.plan_cache_hit`, `prompt.plan_cache_miss`, `prompt.plan_cache_evict`, and `prompt.plan_cache_size` record cache health.

## Headroom & reserves

`DefaultTokenBudgetPolicy` controls how much of the model context the planner spends:

| Policy | Local providers | Cloud providers |
| --- | --- | --- |
| System reserve tokens | 1200 | 1200 |
| Completion reserve ratio | 0.15 | 0.20 |
| Safety margin ratio | 0.05 | 0.10 |
| Headroom tokens floor | 512 | 1024 |

The policy classifies a model as “local” when its key contains `ollama`, `lmstudio`, or `lm-studio`. Headroom applies even when the tokenizer overestimates; trims are logged via `prompt.headroom_violation` and the planner invalidates any cached entry tied to the affected fingerprint.

## Glossary

- **Style coverage** — Map of sampled styles to how many artists/albums contributed. Used to keep prompts representative.
- **Relaxed matching** — Expansion pass that widens style matches when the library is sparse; bounded by `SamplingMaxRelaxedInflation`.
- **Headroom** — Reserved token budget that protects the completion portion of the prompt. Configurable via the token policy.
- **Plan fingerprint** — SHA-256 hash of ordered artist/album selections. Cache invalidation uses it to drop stale plans.

Pair this page with [docs/tokenization-and-estimates.md](./tokenization-and-estimates.md) for token budgeting details and with the optional [architecture overview](./architecture.md) for diagrams of the flow.
