
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Advanced Settings

> Advanced options appear under **Import Lists → Brainarr → Advanced** (and the hidden section). Default values live in `Brainarr.Plugin/BrainarrSettings.cs`; unit tests in `Brainarr.Tests/Services/Prompting` pin expected behaviour. Treat those as the canonical source when adjusting settings or updating docs.

## Quick reference map

| Category | What it controls | Where to read more |
|----------|------------------|--------------------|
| Prompt budgets & compression | Token headroom guard, fallback trimming, prompt compression state | [README](https://github.com/RicherTunes/Brainarr/blob/main/README.md); `Brainarr.Plugin/Services/Prompting/LibraryAwarePromptBuilder.cs`; tests in `TokenBudgetGuardTests` |
| Sampling shape & discovery | Ratios for similar/adjacent/exploratory, album caps, relaxed expansion | `BrainarrSettings.cs` (`SamplingShape`), planner tests (`LibraryPromptPlannerTests`), notes in [`docs/PROVIDER_GUIDE.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/PROVIDER_GUIDE.md) |
| Safety gates & review queue | Minimum confidence, MBID enforcement, Queue Borderline Items | [Review Queue](Review-Queue.md), [`docs/troubleshooting.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/troubleshooting.md) |
| Concurrency & throttling | Provider concurrency caps, adaptive throttling, cooldowns | [Observability & Metrics](Observability-and-Metrics.md), limiter tests in `PlanCacheTests` |
| Deterministic planning & caching | Stable seed generation, plan cache capacity/TTL, fingerprint invalidation | [README](https://github.com/RicherTunes/Brainarr/blob/main/README.md), `CacheSettings.cs`, `PlanCacheTests` |
| Provider fallbacks | Priority lists, failover thresholds | [Cloud Providers ▸ Multi-Provider Strategy](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers#multi-provider-strategy) |

## Field reference

The **More info** link under each field in Lidarr lands on one of the sections below. Each one says what
the field does and where the canonical detail lives — per the operating guidelines further down, values
are not duplicated here, they stay in [`docs/configuration.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md)
and `Brainarr.Plugin/BrainarrSettings.cs`.

### Recommendations

Target number of albums per run. Brainarr treats it as a *target*, not a promise: duplicates against
your library, validation, and the safety gates can all reduce the delivered count — the run summary
reports `attainment` separately from provider success. If you consistently land under target, see
[Iterative top-up](#iterative-top-up) and [Safety gates](#safety-gates).
Range and default: [Settings reference](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#settings-reference).

### Recommendation type

Whether Brainarr returns specific albums, artists only, or a mix. Artist-only mode imports every album
by a recommended artist, so it grows a library much faster than album mode.
Details: [Recommendation modes explained](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#recommendation-modes-explained).

### Discovery mode

How far from your existing taste Brainarr is allowed to roam (`Similar` → `Adjacent` → `Exploratory`).
Note that the top-up loop may *widen* this one step on its own when it keeps hitting duplicates, to break
out of a saturated cluster.
Details: [Discovery modes explained](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#discovery-modes-explained).

### Library sampling

How much of your library is summarised into the prompt (`Minimal` / `Balanced` / `Comprehensive`).
Bigger samples give the model more context but cost prompt tokens, which the budget guard may then trim.
Details: [Sampling shape](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#sampling-shape)
and [Tokenization and estimates](https://github.com/RicherTunes/Brainarr/blob/main/docs/tokenization-and-estimates.md).

### Model selection

The model used for recommendations. For local providers (Ollama, LM Studio) the list is detected live from
the running server; for cloud and subscription providers it is the set that provider accepts.

Lidarr's settings form only loads this list when the page opens — it does **not** refetch when you change
only the *AI Provider* dropdown. So after switching provider the list can still show the previous
provider's models: press **Test**, **Save**, then reopen the import list and the correct models appear.
See also [Provider basics ▸ Choosing a provider](Provider-Basics.md#choosing-a-provider).

### Auto-detect model

Queries the provider for its model list during **Test** so the dropdown stays current. Leave it on for
local providers, where you change models by pulling them into Ollama/LM Studio rather than editing Brainarr.

### Manual model override

Exact API model id to send, bypassing the dropdown. Use it for a model your provider offers but Brainarr's
list does not know yet. It must be the id the provider expects on the wire (`gpt-4o-mini`, not the friendly
label) — a provider that only accepts a fixed set of ids may reject or replace an unknown value, and it
logs a warning when it does.
Details: [Manual model override](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#manual-model-override).

### Timeouts

**AI Request Timeout (s)** caps a single provider request. It also sizes the overall fetch budget, which
scales with the number of top-up iterations.

Raise it to **60–90s for subscription and reasoning models** (OpenAI Codex, Z.AI Coding GLM-5.x, Claude
Code): these think before answering, a full recommendation list takes roughly 15s or more, and a request
that hits the deadline is cancelled mid-stream — so there is no partial response to salvage and the run
returns nothing. Local providers are handled separately: Ollama and LM Studio are given a much longer
per-request timeout when this value is left near the default.
Details: [Timeout settings](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#timeout-settings)
and [Timeouts and Retries](Timeouts-and-Retries.md).

### Iterative top-up

**Top-Up When Under Target** lets Brainarr make additional provider calls when a run comes in under your
target, and **Top-Up Stop Sensitivity** decides how quickly it gives up on a provider that keeps returning
duplicates. Each top-up round is a real provider request, so it costs time and (on metered providers) money.
Tuning knobs: [Hysteresis controls](#hysteresis-controls). Intensity preset: [Backfill strategy](#backfill-strategy).

### Hysteresis controls

The stop conditions for the top-up loop: maximum iterations, how many zero-yield rounds end it, how many
low-yield rounds end it, and the cooldown between rounds. They exist to stop Brainarr hammering a provider
that has run out of new ideas for your library. Growth of the iteration budget is capped internally, so a
provider that dribbles one item per round cannot extend a run indefinitely.

### Backfill strategy

Preset intensity for the top-up loop (`Off` → `Conservative` → `Standard` → `Balanced` → `Aggressive`),
which is the easy way to tune top-up without touching the individual
[hysteresis controls](#hysteresis-controls).
Details: [Backfill strategies explained](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#backfill-strategies-explained).

### Guarantee exact target

Keeps working until the target count is met rather than accepting a short run. This makes runs longer and
more expensive, and on a library that has already absorbed a provider's obvious suggestions it can spend
its whole budget rediscovering duplicates. Verify the result through the
[Review Queue](Review-Queue.md) before relying on it.

### Safety gates

The filters applied after the model answers and before anything reaches Lidarr:

- **Minimum Confidence** — drops items scored below the floor. Items the model did not score at all are
  *kept*, not silently cut, so a score-less provider is not gated to zero. When this gate is what kept a
  run under target, the run summary names it.
- **Require MusicBrainz IDs** — demands a resolved MBID, which is the strongest defence against
  hallucinated releases.
- **Queue Borderline Items** — sends the items a gate rejected to the [Review Queue](Review-Queue.md)
  instead of discarding them, so you can approve or reject them yourself.

Tightening these gates lowers delivered counts by design; check the Review Queue before loosening them.
Details: [Validation and filtering](https://github.com/RicherTunes/Brainarr/blob/main/docs/configuration.md#validation-and-filtering).

## Operating guidelines

1. **Keep defaults in sync.** When changing an advanced option (code or UI), update `BrainarrSettings.cs` and corresponding tests first, then refresh this page with pointers rather than duplicating raw values.
2. **Document rationale.** Record notable overrides (e.g., custom `sampling_shape`, adaptive throttling tweaks) in [`docs/VERIFICATION-RESULTS.md`](https://github.com/RicherTunes/Brainarr/blob/main/docs/VERIFICATION-RESULTS.md) alongside the release/verification notes.
3. **Validate with the Review Queue.** If you tighten Safety Gates or enable Guarantee Exact Target, verify behaviour via the Review Queue workflow before rolling to production.
4. **Monitor drift.** Use the Observability preview or Prometheus endpoint (`metrics/prometheus`) to make sure new limits keep latency and 429 rates within expected bounds.

Need field-by-field help? Open Brainarr in Lidarr, hover the info icons in the Advanced tab, and cross-check against the code/test references above. This keeps the UI hints, wiki, and implementation aligned without maintaining duplicate tables.
