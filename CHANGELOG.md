# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

### Fixed (perf — `Album.ArtistId` N+1 lazy-load OOM + library-profile fallback on large libraries — 2026-07-03)

- **Live-found (~11,700-artist library): `System.OutOfMemoryException` 18x/hour** from `NzbDrone.Core.Music.ArtistRepository.Query` → `LazyLoaded.LazyLoad()` → `Album.get_ArtistId()` → `DuplicateFilterService.FilterExistingRecommendations`. `Album.ArtistId` is a compatibility shim (`Artist?.Value?.Id ?? 0`) over a `LazyLoaded<Artist>` that `IAlbumService.GetAllAlbums()` leaves unloaded (it's a plain unjoined `SELECT * FROM Albums`); reading it inside a `foreach (var album in existingAlbums)` loop fires one full per-row `ArtistRepository.Query()` DB round trip **per album** — an N+1 that thrashes memory at scale. Fixed in `DuplicateFilterService` (`FilterDuplicates` + `FilterExistingRecommendations`), `LibraryContextBuilder.BuildProfile`, and `StyleContextBuilder` (both sequential and parallel aggregation paths) by keying/joining on `Album.ArtistMetadataId` / `Artist.ArtistMetadataId` instead — plain `int` columns needing no lazy load, and (per `NzbDrone.Core.Datastore.TableMapping`'s actual `Album.Artist` lazy-load registration) exactly the join key the lazy load itself would have used, so results are provably unchanged.
- **Same live session also logged `Warn|Brainarr|Failed to get real library data, using fallback: Error parsing column 21 (Links=[...])`.** Root-caused to the *same* per-row query: it joins `Artists`+`ArtistMetadata` (which carries the `Links` embedded-JSON column), and `LibraryContextBuilder.BuildProfile` wraps its entire body in one try/catch — so a Dapper column-mapping fault on that narrow per-row query (not the bulk `GetAllArtists()`/`GetAllAlbums()` scan) discarded the whole real profile in favor of the hardcoded 100-fake-artist fallback. Eliminating the per-row query removes the trigger.
- TDD: `Brainarr.Tests/Services/Core/AlbumArtistLazyLoadTestDoubles.cs` adds a `RecordingArtistLazyLoaded` test double (a public `LazyLoaded<Artist>` subclass simulating the real internal lazy-load proxy, with an optional simulated failure) + `LazyLoadCounter`. New regression suites (`DuplicateFilterServiceNPlusOneTests`, `LibraryContextBuilderNPlusOneTests`, `StyleContextBuilderNPlusOneTests`) prove RED (2,000 per-album DB round trips for 2,000 albums, scaling 1:1) → GREEN (0 round trips regardless of album count, and `BuildProfile` returns the real profile instead of the fallback even when the per-row query would fail). One pre-existing test (`LibraryContextBuilderTests`) had fixtures built only via the legacy `Album.ArtistId =`/`Artist.Id =` shortcut without setting the real `ArtistMetadataId` FK; updated to set `ArtistMetadataId` explicitly (matching real Lidarr data). Full suite green (3,271 passed / 0 failed / 9 skipped, excluding quarantined stress tests).
- See `CLAUDE.md` → "Large-library performance: never read `Album.ArtistId` on host-fetched albums" for the full mapping-source citation and a documented list of other `Album.ArtistId`-in-a-loop sites (`LibraryAnalyzer`, `LibraryPromptPlanner`/`LibraryPromptRenderer`, `DefaultSamplingService`, `AdvancedDuplicateDetector`, `SimpleRecommendationValidator`) that share the same hazard but were not on this crash's call path — left as a follow-up sweep.

### Dependencies (2026-07-03)

- `ext/Lidarr.Plugin.Common` submodule re-pinned to **`a894567d`** (`commonVersion` **`1.18.0-dev`**) so Brainarr stays on the current Common mainline after the template-scaffold CI gates and SettingsBinder malformed-Guid preservation fix. No Brainarr source changes required.

### Removed (parity convergence — delete dead/speculative security sanitizers; keep the Common-aligned `PromptSanitizer` seam — 2026-05-31)

- **Removed `InputSanitizer` (~462 lines + its dedicated tests), the orphaned `UrlSanitizer` log-scrubber (CorrelationContext), and the dead `SecureHttpClient.SanitizeErrorMessage`** — all unused (zero production callers, verified by repo-wide sweep). brainarr deliberately does **not** sanitize prompts in production: the only "untrusted" input is the user's *own* library names + typed styles, so prompt-injection isn't a real vector — which is why `InputSanitizer` (and the `LlmPromptSanitizer`-backed shim) were never wired. Per a product decision, kept the thin `PromptSanitizer` → Common `LlmPromptSanitizer` seam as a ready-to-wire convergence point should an untrusted-input scenario ever arise. `SecureHttpClient.SanitizeUrl` was deliberately **retained** (Common's `Sanitize.UrlHostOnly` returns a bare host, not the `scheme://host` form the debug log uses — not an exact behavioral match, so adopting it would be a cosmetic change for no gain). Build clean; full suite green (3091 passed / 0 failed, excluding quarantined). Found by the Common-helper parity sweep (#2).

### Removed (parity convergence — delete orphaned hand-rolled `MusicBrainzRateLimiter` — 2026-05-31)

- **`MusicBrainzRateLimiter` (~290 lines + 2 test files) was orphaned dead code.** It is a hand-rolled rate limiter (semaphore + timestamp queue + cleanup `Timer` + own 429-retry) with **zero production callers** — the live MusicBrainz path (`MusicBrainzService`) already converged on Common's token-bucket limiter via `RateLimiter.ExecuteAsync("musicbrainz", …)` + `RateLimiterConfiguration.ConfigureDefaults`, leaving this class a divergent orphan (it even carried a different `1rps + 50/min` contract a maintainer could wire up by mistake, plus a process-lifetime `Timer` + static singleton). Proven unreferenced via a repo-wide sweep; removed the class + `MusicBrainzRateLimiterTests` + `MusicBrainzRateLimiterExtendedTests`. Build clean; full suite green (3184 passed / 0 failed, excluding quarantined). Found by the Common-helper parity sweep (#2).

### Fixed (top-up — now actually fills the deficit under RequireMbids: exclude delivered + MBID-enrich top-up recs — 2026-05-31)

- **The iterative top-up contributed *zero* net recommendations under `RequireMbids`** (live-confirmed: a deeper-style run delivered 8/10 with `top-up returned=0`, and once the library held the popular artists it would have delivered ~2/10). Two compounding gaps: **(T1)** the already-delivered initial-batch recommendations were never threaded into the top-up prompt's `[[SYSTEM_AVOID]]` list or the strategy's dedup baseline (`existingKeys`), so a saturated provider re-emitted delivered artists — the strategy counted them "new" (they are not in the *library*) and they were only dropped post-hoc, wasting the iteration; **(T2)** top-up recs arrive from the provider without MBIDs and were never enrichment-resolved on the top-up path (initial-batch enrichment runs *before* top-up), so `TopUpPlanner`'s require-MBID filter dropped them all. The two compound: fixing only one leaves the other as the blocker. Fix threads the delivered set from `RecommendationPipeline` → `TopUpPlanner` → `IterativeRecommendationStrategy` (seeding both `[[SYSTEM_AVOID]]` and `existingKeys`, additive to the existing rejected-item exclusions — no F3 regression), and MBID-enriches top-up recs via the **same** `IArtistMbidResolver` the initial batch uses (injected as a method param, before the require-MBID filter) so resolvable artists survive while genuinely-unresolvable ones still drop. The planner summary now attributes every drop (`enrichment-dropped`/`no-MBID removed`/`library-duplicates removed`) so it reconciles exactly, and the strategy's per-iteration log says "new (not already in library/delivered/rejected)" instead of the misleading "unique". **Live-verified on lidarr-e2e: the same deeper-style run went from 80%→100% attainment with `top-up returned` 0→8** (all 8 new artists MBID-resolved, `no-MBID removed=0`). TDD: red→green (3 tests fail with both behaviors disabled, pass after); RecommendationPipeline mocks updated for the new signature. Adversarially reviewed (clean verdict). Found by live-usage log mining (T1/T2).

### Fixed (test flake — RunSyncWithTimeout timeout race widened — 2026-05-31)

- **`AsyncHelperTests.RunSyncWithTimeout_ExceedsTimeout_ThrowsTimeoutException` flaked under full-suite load** (passed in isolation): `RunSyncWithTimeout` races the task against `Task.Delay(timeout)` via `WhenAny`, and with the test's 2x margin (200ms task vs 100ms timeout) a starved thread pool could observe the 100ms timer's completion *after* the 200ms task finished, so no `TimeoutException` was thrown. Widened the task to 30s (a ~300x margin) so the timeout always wins regardless of load; the orphaned delay is harmless (abandoned once the timeout fires at ~100ms, so the test still completes in ~100ms). Test-only change.

### Removed (dead code — legacy UI action-handler classes superseded by the orchestrator — 2026-05-31)

- **Deleted three dead action-handler types (~1,079 lines incl. tests).** `ImportListActionHandler` was referenced nowhere; `ModelActionHandler` + `IModelActionHandler` were referenced only by their own two test files. The live UI-action dispatch is `BrainarrOrchestrator.HandleAction` — an inline `switch` delegating model options/detection to `ModelOptionsProvider` and review actions to `ReviewQueueActionHandler`, reached via `BrainarrImportList.RequestAction` — and it never routes to either handler class. The `providerChanged` action (#89) was dead in all three places: it is **absent from the orchestrator switch entirely**, and where it survived on the dead handlers it only cleared `DetectedModels`, which `getModelOptions` already does on every call. `testConnectionDetails` was likewise reachable only through the dead `ModelActionHandler` (no live caller — verified by a repo-wide sweep). Proven unreferenced before deletion (every type name swept across the repo); build clean and full suite green (3194 passed / 0 failed, excluding quarantined) with all 5 files removed. Closes #89.

### Fixed (tokenizer — fallback WARN now fires once process-wide, not once per run — 2026-05-31)

- **The "Tokenizer fallback: no tokenizer registered for …" WARN re-fired on every recommendation run** (observed in live Lidarr logs spamming for `zaicoding:glm-4.5-air` across 08:35/08:36/08:43/08:58/09:00). Root cause: `ModelTokenizerRegistry._fallbackWarn` was an **instance** field, but Lidarr re-instantiates `BrainarrImportList` — and thus a fresh per-instance DI `ServiceProvider` + a fresh `ModelTokenizerRegistry` — per operation, so the `WarnOnce` gate reset every run and the WARN never deduped across runs. Common's `WarnOnce` is correct and is explicitly documented for `private static readonly` usage; brainarr was holding it per-instance (the anti-pattern). Made `_fallbackWarn` `static` (process/ALC-wide), so the WARN fires once and subsequent fallbacks (same run or later) drop to Debug — matching the documented intent. brainarr-local fix; no Common change needed. Regression guard: a new test asserts the WARN fires exactly once across **two** registry instances (red before the fix: 2 warns; green after: 1); existing fallback-warn/metric tests reset the now-static gate in their ctor (the fallback keys used are exclusive to that test file, so no cross-collection race). Full suite green (3185 passed / 0 failed, excluding quarantined). Found by live-usage log review (F1).

### Fixed (parsing — JSON-parse failure logs at WARN only on total loss, not when salvage recovers — 2026-05-31)

- **`Failed to parse recommendations JSON` logged at WARN on every GLM iteration**, even though `SalvageObjectsFromText` recovered the recommendations immediately after (artists were accepted). Verbose models such as Z.AI/GLM routinely truncate at `max_tokens`, so the *primary* parse throws — but the salvage pass recovers the complete objects before the cut. The WARN fired unconditionally at the point of primary failure, before that recovery was known, producing misleading log spam on successful runs. Now the primary-parse exception is **captured**, not logged immediately; after the fallback-extraction + object-salvage passes run, the level is decided from the **outcome**: `Debug` ("…recovered N item(s) via fallback/salvage; not warning") when ≥1 recovered, reserving `WARN` ("0 recovered after fallback + salvage") for genuine total loss. The secondary in-`catch` "Fallback array extraction also failed" was likewise downgraded to `Debug` (object salvage still runs after it). The salvage path itself is unchanged. Regression guards: a truncated fenced GLM payload recovers 2 recs with **no** WARN (red before: WARN fired); a non-JSON payload that recovers 0 **still** WARNs. Full suite green (3186 passed / 0 failed, excluding quarantined). Found by live-usage log review (F2).

### Fixed (settings UX — Configuration URL shows the real endpoint per provider; accurate model-refresh guidance — 2026-05-31)

- **`Configuration URL` showed `"N/A - API Key based provider"` for every cloud/subscription provider** (including Z.AI Coding), so switching provider never surfaced where requests actually go. It now displays the **real endpoint per provider** for reference — e.g. Z.AI Coding → `https://api.z.ai/api/anthropic/v1/messages`, OpenAI → `…/v1/chat/completions`, Anthropic → `…/v1/messages`, Gemini, Groq, DeepSeek, OpenRouter, Perplexity, the subscription providers, etc. Local providers (Ollama/LM Studio) remain editable server URLs; cloud endpoints are display-only (the setter still no-ops for them and the runtime endpoint is owned by each provider), so this is informational and changes no request behavior. (#88)
- **Corrected the `Model Selection` help text**, which told users to *"edit the Configuration URL to trigger an inline refresh"* — that never worked, because Lidarr's `EnhancedSelectInputConnector` only refetches a dynamic-select's options when a field named exactly `baseUrl`/`apiPath`/`apiKey` changes (and only on dropdown re-open), and brainarr's field is `configurationUrl`. It **never watches the Provider field at all**. The help text now states the accurate flow: after switching provider, entering/updating the **API Key** (a watched field) refreshes cloud/subscription model lists; otherwise Save & reopen. (#87)
- Added `BrainarrConstants.PerplexityChatCompletionsUrl` (shared endpoint constant for the display). Pinned by tests: each cloud provider's `ConfigurationUrl` returns its real `https` endpoint (not "N/A"); local providers stay editable. Full suite green (3192 passed / 0 failed, excluding quarantined). Found by live-usage report (F5). *Note: making the model list refetch on a Configuration-URL edit (renaming the field to the watched `baseUrl`) is tracked separately as it merges with an existing internal `BaseUrl` property.*

### Fixed (styles — Music Styles now actually selectable: free-text Tag + server-side resolution — 2026-05-31)

- **Music Styles selections never stuck.** Live-confirmed root cause (verified against Lidarr's frontend source): the field was a `TagSelect` with a `SelectOptionsProviderAction`, but Lidarr's `TagSelectInputConnector` **ignores the provider action entirely** — it renders only static, numeric-keyed `selectOptions` (which `SchemaBuilder` leaves **empty** whenever a provider action is set) and its `onTagAdd` **rejects any tag not already in that (empty) list**. So the dynamic, string-slug style catalog was never delivered to the input and *no style could ever be added* (`styleFilters.selectOptions` count = 0 in the live schema). Lidarr has no generic dynamic-multi-select input (the `device`/`playlist` connectors hardcode their own actions; `DynamicSelect` is single-select). Fix: switched Music Styles to a **free-text `Tag`** field — anything typed sticks (string array, matching `IEnumerable<string>`) — and made `StyleCatalogService.ResolveSlugInternal` **token-aware**: a typed name resolves to its catalog slug by exact slug/name/alias first, then by a **unique token-AND** match (so `"Rock Alternative"` → `alternative-rock` despite the word-order swap), staying unresolved (→ freeform style anchor) when ambiguous rather than guessing. Pinned by tests: reordered/exact free-text resolves to the canonical slug; ambiguous/unknown stays unresolved; full suite green (3187 passed / 0 failed, excluding quarantined). Found by live-usage report + on-instance verification (F4). *(Supersedes the UI relevance of the earlier server-side search-scoring tweak, which the TagSelect never invoked.)*

### Removed (repo hygiene — stop tracking the `_plugins/` build-artifact dir — 2026-05-31)

- **`_plugins/Brainarr.Plugin/` had 229 committed files** — a full extracted plugin-output directory dominated by *host* assemblies (`Lidarr.Core.dll`, `FluentValidation.dll`, `Azure.*`, `DryIoc.dll`, …) plus the merged `Lidarr.Plugin.Brainarr.dll`. These are build artifacts, not source: nothing in the repo *writes* into `_plugins/` (no script/build step; `ecosystem-parity-lint.ps1` already excludes it as artifacts), and the E2E `BrainarrLidarrContainerFixture` reads it only as the **last** of four DLL-discovery candidates — after `Brainarr.Plugin/bin/{,Release,Debug}/`, which `scripts/e2e.ps1` populates on every supported run — so the committed copy was redundant and could drift to a stale/wrong-ABI DLL. `git rm --cached`'d all 229 (working tree preserved — files stay on disk, so the fallback still resolves locally and the machine-specific `ext/.../PackageClosureTests` absolute-path reference is unaffected) and added `_plugins/` to `.gitignore`, matching the already-ignored `Brainarr.Plugin/bin/`. No source/test code changed (tracking-only change; the suite is unchanged from the prior green run, 3180 passed / 0 failed). Found by the fifth A→Z convergence re-audit (#79).

### Fixed (history — dedup key now collapses whitespace — 2026-05-31)

- **`RecommendationHistory.GetKey` keyed on whitespace-significant text**, so a stray double space or leading/trailing space (common in scraped or model-emitted names) — e.g. `"The  Beatles"` vs `"The Beatles"` — produced different dedup keys, tracking the same artist under two keys and slipping the re-suggestion / over-suggested / recently-rejected exclusions. Now each key part collapses leading/trailing and internal whitespace runs to a single space (via `Split((char[])null, RemoveEmptyEntries)` + join — no regex, no ReDoS surface, consistent with `RecommendationValidator`), applied alongside the existing `HtmlDecode` + lowercase. Same consistency rationale as the #62 entity decode. Migration impact is negligible (whitespace-anomalous names are rare; a re-suggestion simply re-keys under the normalized form). Pinned by a Theory asserting double-space / surrounding-space / tab / entity+space variants all key identically across both artist and album parts; full suite green (3180 passed / 0 failed, excluding quarantined). Found by the fifth A→Z convergence re-audit (#78).

### Hardened (Anthropic — clamp temperature to its [0,1] range — 2026-05-30)

- **`BrainarrAnthropicProvider` sent `temperature` unclamped**, but Anthropic's valid range is `[0, 1]` (unlike OpenAI's `[0, 2]`). A value above 1 would 400 the request. Now clamped via `Math.Clamp(…, 0.0, 1.0)` on both the `CompleteAsync` and `StreamAsync` bodies. Defensive: no current caller sends out-of-range (the adapter default is 0.8 and `LMStudioTemperature` only feeds LM Studio), so this is future-proofing against a misconfiguration — mirrors the LM Studio temperature clamp (#70). Verified by construction + the existing Anthropic `CompleteAsync` tests (no regression; full suite 3176 passed / 0 failed). **Deferred (heuristic b):** the sibling task to move Gemini's system prompt from user-part concatenation to the native `systemInstruction` field is NOT done — the field name is ambiguous across the generateContent REST surface (`systemInstruction` vs `system_instruction`), Gemini silently ignores unknown JSON fields, so a wrong guess would silently DROP the system prompt (worse than the working concatenation), and it can't be live-verified without a Gemini key. The current concatenation is functional; revisit with a real Gemini key. (#48)

### Tested (cost — pin TokenCostEstimator usage-history cap + add reset hook — 2026-05-30)

- **`TokenCostEstimator.UsageHistory` (a process-lifetime static list, capped at 10k + 30-day TTL) had no regression test for the cap and no way for tests to reset it.** Added internal `ResetUsageHistoryForTesting()` / `UsageHistoryCountForTesting` hooks and a test that drives the history past the cap and asserts it's trimmed to exactly the cap (oldest evicted) — closing the last untested long-lived-collection bound. The test is isolated in a `DisableParallelization` collection: driving past a 10k cap is inherently O(n²) (every `StoreUsageReport` does a full-list `RemoveAll`), and that CPU burst — when run in parallel — starved an unrelated wall-clock timing test into a flake (and the static list would pollute parallel cost tests). Same isolation pattern as `LimiterRegistryBoundedCollection`. Verified green across two consecutive full-suite runs (3176 passed / 0 failed, excluding quarantined). No production behavior change. (#57)

### Hardened (sanitizer — rebuild via record `with` so no field is silently dropped — 2026-05-30)

- **`RecommendationSanitizer` rebuilt each `Recommendation` field-by-field (`new Recommendation { … }`)**, which resets every member not explicitly listed. It listed 9 of the record's 14 fields — so `ReleaseYear`, `Source`, `Provider`, `MusicBrainzId`, and `SpotifyId` were reset to default. (No data loss today, since the JSON parser doesn't populate those at the point the sanitizer runs — but it's a latent footgun: any field set by a pre-sanitize stage, or any field added to the record later, would silently vanish — exactly how the `ConfidenceProvided` provenance cliff was originally introduced.) Converted to `rec with { …only the transformed fields… }`, so every other member (incl. `ConfidenceProvided`) auto-carries. Matches the documented "prefer `with` over field-by-field rebuilds" rule already followed by the MBID resolvers. Pinned by a test asserting all non-transformed fields survive sanitization; full suite green (3174 passed / 0 failed). Found by the A→Z convergence re-audits (#64).

### Fixed (history — dedup key now HtmlDecodes entities — 2026-05-30)

- **`RecommendationHistory.GetKey` keyed on the raw (entity-encoded) artist/album**, so an entity-encoded `"Simon &amp; Garfunkel"` and the raw `"Simon & Garfunkel"` produced different keys — the same entity was tracked under two keys and could slip the re-suggestion / over-suggested / recently-rejected exclusions. Now it `HtmlDecode`s before lowercasing (mirrors the MBID-resolver fixes #60/#66) and is null-safe (a null artist previously threw `NullReferenceException`). Migration impact is negligible (entity-bearing names are rare post-#60; a re-suggestion simply re-keys under the decoded form). Pinned by tests: encoded/raw spellings produce the same key; null artist doesn't throw. (The sibling validation-path `MusicBrainzService` query has the same un-decoded gap but is part of the *unwired* async-validation stack — dead code, tracked separately — so the live MB query paths, already fixed in #60/#66, are unaffected.) Found by the A→Z convergence re-audits (#62).

### Removed (dead code — MinimalResponseParser, PlanCacheStatistics — 2026-05-30)

- **Deleted two never-wired classes.** `MinimalResponseParser` (an alternative ultra-minimal AI-response parser) had no production consumer — the live parser is `RecommendationJsonParser` (with truncation salvage); only its own two test files exercised it. `PlanCacheStatistics` (a fully-implemented `ICacheStatistics`) was `internal`, never instantiated, untested, and never injected into any plan cache. Removed both classes + `MinimalResponseParser`'s tests, and tidied the now-inert references (a coverage `Exclude` pattern in `test.fast.runsettings`, the `prod_files.txt` listing). No behavior change. Full suite green (3170 passed / 0 failed, excluding quarantined). (Two sibling tested-but-unused utilities — `ConcurrentCache` and `FormatPreferenceCache` — are deferred to a focused keep-vs-delete decision since they carry their own test suites and `ConcurrentCache` underpins the quarantined cache stress tests.) Found by the A→Z convergence re-audits (#49).

### Fixed (dedup — "&"/"+" now unify with "and" in artist/album normalization — 2026-05-30)

- **The duplicate detector didn't treat "Simon & Garfunkel" and "Simon and Garfunkel" as the same.** `AdvancedDuplicateDetector`'s artist/album normalizers stripped `&`/`+` to spaces (via the `[^\w\s]` pass) without unifying them to the word "and", so `"… & …"` normalized to `"… …"` while `"… and …"` kept the word — the two never matched. Now both normalizers replace `&`/`+` with a spaced `" and "` *before* punctuation stripping (spaced so tokens never glue — `"A&B"` → `"a and b"` — with the existing whitespace-collapse tidying up). Symmetric (both dedup sides run through the same normalizer) and low-risk: it only merges spellings of the same entity, introducing no new collision between distinct artists beyond the existing alphanumeric normalization. Pinned by tests: `&`/`+`/`and` spellings of a name all normalize identically; no token-gluing. (Pre-existing gap surfaced by the #68 dedup-normalization work; the former dead `"&"->"and"` map entry never fired because it ran *after* the punctuation strip.)

### Hardened (resilience — circuit-breaker registry is now bounded — 2026-05-30)

- **`CommonBreakerRegistry._breakers` was a plain unbounded `ConcurrentDictionary`** on a process-lifetime singleton, while its sibling `LimiterRegistry` already caps the *identical* `ModelKey` (provider+model) space with a `BoundedConcurrentDictionary`. Cardinality is low in practice (the few provider/model combos a user configures), so it was never a real leak — but it was a gap in the "every long-lived in-memory collection is verified bounded" invariant. Switched it to `BoundedConcurrentDictionary` at the same 5120 cap so the bound is a backstop that upholds the invariant (eviction relies on Common's already-tested behavior and won't fire in normal use). `GetOrAdd` semantics unchanged; 32 existing breaker tests still green. Found by the fourth A→Z convergence re-audit (growth dimension).

### Fixed (observability — default provider/model filters now applied to the metric views — 2026-05-30)

- **`Observability Provider Filter` and `Observability Model Filter` were dead settings** — declared (and Hidden) but never consumed. The observability service already filters its metric series on `query["provider"]`/`query["model"]`, so these settings were meant to supply *defaults* when a request omits them; they just weren't wired. Now `BrainarrOrchestrator` merges them into the `observability/get` and `observability/html` views via `WithObservabilityFilterDefaults` (an explicit request filter always overrides the default; `observability/getoptions`, the picker that lists all series, is deliberately left unfiltered). Both fields un-hidden (visible Advanced). Hardened against a pathological query with case-colliding duplicate keys (built last-wins instead of the throwing copy-constructor) and made case-insensitive so an explicit `Provider` (any case) isn't clobbered. Adversarial review's "comparer mismatch clobbers explicit filter" HIGH was refuted (the OrdinalIgnoreCase copy makes the original key case-insensitively findable), but its adjacent duplicate-key-throw edge was real and is now guarded. Pinned by tests: defaults applied when omitted, explicit (case-insensitive) request wins, no-settings leaves the query unfiltered, duplicate keys don't throw, caller's query not mutated. Found by the A→Z convergence re-audits (correctness/dead-config).

### Removed (settings — three dead stub properties — 2026-05-30)

- **Deleted `EnableFallbackModel`, `FallbackModel`, and `EnableLibraryAnalysis`** from `BrainarrSettings` — vestigial properties (under an "Additional missing properties" comment) with **no FieldDefinition** (never shown in the UI) and **no production consumer** (verified: their only `.member` access was the declarations themselves; library analysis runs unconditionally and there is no fallback-model logic gated by these). Removed the properties + their cov-tests + two inert test-initializer assignments. Safe by construction (removing a property is ignored on load of an existing config — STJ skips unknown keys), same as the earlier `PreferStructuredJsonForChat` removal. Found by the fourth A→Z convergence re-audit (correctness/dead-config dimension). (The re-audit also re-flagged the already-queued `ObservabilityProviderFilter`/`ObservabilityModelFilter` unapplied filters — tracked under the existing observability-filter LOW, not re-counted.)

### Fixed (resilience — top-up cool-down now honors run cancellation — 2026-05-30)

- **The local-provider cool-down in the iterative top-up loop ignored the cancellation token.** When an Ollama/LM Studio run hit the low/zero unique-rate early-stop, `IterativeRecommendationStrategy` did `await Task.Delay(cooldownMs)` with no token, so a cancelled run blocked for the full cool-down before the cancellation could propagate. Now `Task.Delay(cooldownMs, cancellationToken)` — on cancel it throws `OperationCanceledException`, which the loop's existing `when (cancellationToken.IsCancellationRequested)` guard re-throws as a run-cancellation (the orchestrator maps it to an empty result), exactly as the rest of the chain does; with no cancellation (today's shipped sync `Fetch()` path uses `CancellationToken.None`) behavior is unchanged. Found by the fourth A→Z convergence re-audit (resilience dimension). The cancellation *contract* is covered by the existing propagation tests; the *bug class* (an un-tokened `Task.Delay` on the hot fetch path) is pinned by a new source-guard test, since a behavioral test for that exact branch would be timing-fragile.

### Fixed (settings — LM Studio Temperature now actually applies (+ clamped); removed dead LM Studio Max Tokens — 2026-05-30)

- **`LM Studio Temperature` was a dead setting** — `ProviderRegistry` built the LM Studio adapter with no temperature, so it ran at the generic `LlmProviderAdapter` default of **0.8** regardless of the setting (which defaulted to 0.5, with help text recommending 0.3–0.7 for curation, and the provider's own fallback being 0.5 — three LM-Studio-specific signals all pointing at 0.5). Now wired: `ProviderRegistry` passes `settings.LMStudioTemperature` to the adapter, so LM Studio honours it. **Behavior change:** LM Studio's effective default temperature is now **0.5** (was 0.8) — more deterministic/focused curation; raise it for more variety. Field un-hidden (visible Advanced). Hardened with a fail-soft clamp to the OpenAI-compatible `[0.0, 2.0]` range (the field has no range validator, so an out-of-range value would otherwise reach the API and 400/degenerate); an out-of-range value is clamped with a warning log. Adversarial review flagged exactly this unvalidated-range gap (the only HIGH; introduced by making the setting live) — fixed by the clamp. Pinned by tests: the configured temperature reaches the adapter, the default is 0.5, cloud providers keep 0.8, and out-of-range inputs clamp to [0,2].
- **Removed the dead `LM Studio Max Tokens` setting.** It was read by no production code and is superseded by the timeout-aware output-token budget (`TimeoutContext.GetMaxOutputTokensOrDefault`), which is the documented load-bearing mechanism — a flat per-provider cap would conflict with it (overshooting the request timeout loses the response body). Removed the field + its cov-test (no behavior change; it was never wired). (Found by the third A→Z convergence re-audit; resolves the dead LM-Studio tuning knobs the same way `PreferStructuredJsonForChat` was handled — wire the one with a real consumer, delete the one superseded by a central mechanism.)

### Added (release — version-coherence pre-flight guard — 2026-05-30)

- **The release workflow now fails fast if the release tag disagrees with the committed `VERSION` file / `plugin.json` / `manifest.json`.** Investigation (third A→Z re-audit) confirmed a real drift gap: brainarr's `release.yml` delegates to the shared reusable workflow, which stamps `plugin.json`/`manifest.json` from the tag but does **not** stamp the `VERSION` file — and brainarr's assembly version derives *solely* from `VERSION` (`GenerateAssemblyInfo=true`, no `<AssemblyVersion>` in the csproj, Directory.Build.props reads `VERSION`). So tagging a version that differs from the committed `VERSION` would ship a wrong `installedVersion` in `/api/v1/system/plugins` — the exact 1.3.2-vs-1.4.1 drift `VersionContractTests` was created to prevent, and which nothing caught at release time (the reusable workflow runs no tests). Added a `verify-version` job (which `release` now `needs:`) that resolves the release version (tag or `workflow_dispatch` input), strips the leading `v`, and asserts it equals `VERSION` == `plugin.json` == `manifest.json`, with an actionable error telling you to bump + commit `VERSION` before tagging. Normal flow (bump all three, commit so CI's `VersionContractTests` pass, tag that version) passes; a drifted tag is blocked. The regex is line-anchored so it reads the top-level `version` key only (never `commonVersion`). Validated with `actionlint` + simulated against matching/drifted/prerelease tags; adversarial review's "matches commonVersion" HIGH was refuted (the literal, case-sensitive `"version"` pattern can't match `"commonVersion"`), and its two robustness suggestions (line-anchor + explicit missing-file guard) were applied. *(The deeper ecosystem fix — stamping `VERSION` in the shared reusable workflow so all four plugins are covered — is queued separately; this guard makes brainarr safe regardless.)*

### Fixed (dedup — artist-name normalizer no longer corrupts names containing "ft"/"feat" — 2026-05-30)

- **`AdvancedDuplicateDetector`'s artist-name normalizer mangled any name containing the letters "ft" or "feat".** The step that canonicalizes featuring-abbreviations (`ft`/`feat` → `featuring`, so "X ft Y" dedups against "X featuring Y") used a plain substring `Replace`, so after lowercasing it turned "Daft Punk" → "dafeaturing punk", "Lifton" → "lifeaturingon", "Soft Cell" → "sofeaturing cell", and "Defeat" → "defeaturinguring" — distorting the fuzzy-similarity score used for duplicate detection. Now uses word-bounded `Regex.Replace(@"\bft\b" / @"\bfeat\b", …)`, so only standalone abbreviations are normalized and embedded letters are left alone. Also removed three dead dictionary entries (`&`→`and`, `+`→`and`, `w/`→`with`): the earlier `[^\w\s]` punctuation pass already replaces `&`, `+`, `/` with spaces, so those keys never matched at that point — removing them is a zero-behavior cleanup (verified). Adversarial review confirmed no new edge cases (start/end, repeated, digit/unicode-adjacent all correct), the two `featuring` spellings still unify for dedup, and the result is dictionary-iteration-order independent. Pinned by a new `AdvancedDuplicateDetectorNormalizationTests` (substring names preserved; standalone "ft"/"feat" still normalize; ft/feat/featuring all unify). (Found by the third A→Z convergence re-audit. A separate, pre-existing dedup gap — "Simon & Garfunkel" vs "Simon and Garfunkel" don't normalize alike — is queued as a LOW; it predates this fix.)

### Fixed (validation — Custom Filter Patterns & Strict Validation now actually filter — 2026-05-30)

- **The `Custom Filter Patterns` and `Enable Strict Validation` advanced settings were read but never applied — they did nothing.** The consumer existed and was tested (`RecommendationValidator` has a `(logger, customPatterns, strictMode)` ctor that parses comma-separated substrings and rejects matching album titles / tightens parenthetical-suffix filtering), but the live pipeline used the dependency-injected validator, which `BrainarrOrchestratorFactory` constructs with the logger-only ctor — so a user's patterns and strict toggle never reached it. Root cause is the architecture seam: the validator is a process-wide **singleton** while these are **per-import-list-definition** settings. Fixed by having `RecommendationPipeline` resolve the validator per run (`ResolveValidator`): when `CustomFilterPatterns`/`EnableStrictValidation` are at their defaults it reuses the injected singleton (zero behavior change for the common case), and only when the user configures either does it build a per-run validator carrying those settings. Both fields were **un-hidden** (now visible Advanced, like their sibling validation settings) — wiring a setting that stays UI-unreachable is only half a fix. Custom patterns match as lowercased plain substrings (NOT regex, so no ReDoS surface; blanks dropped), and the help text now warns that an over-broad entry like `the` would filter your whole list. Adversarial review's two "HIGH FieldDefinition Order collision" findings were refuted as false positives — Lidarr's `FieldDefinition(Order)` is a UI display-order hint, not a field identity (settings serialize by property name), and the codebase already ships many duplicate Orders. Pinned by tests: default→injected validator (same reference), custom patterns→a validator that filters them, strict-only→configured validator, and patterns+strict compose. (Found by the third A→Z convergence re-audit; the sibling `PreferStructuredJsonForChat` was the opposite case — no consumer — and was deleted.)

### Removed (settings — vestigial hidden `PreferStructuredJsonForChat` toggle — 2026-05-30)

- **Deleted the `Prefer Structured JSON (schema)` advanced setting, which was dead config.** It was `Hidden` (unreachable through the UI) and *no production code ever read it* — `LlmProviderAdapter` unconditionally sets `JsonMode` from the provider's `JsonMode` capability flag (with an in-code comment stating "brainarr always wants strict JSON output for recommendation parsing"), so the toggle had zero effect. The field contradicted that documented design intent, and `BUILD.md` falsely advertised it as a working toggle. Resolved by removing the field (matching the consumer's explicit always-on design and avoiding an unreachable foot-gun that would only have *degraded* parsing reliability if wired and disabled) and correcting `BUILD.md` to state structured JSON is applied automatically whenever the provider advertises the capability (others fall back to system-prompt JSON shaping; `StructuredJsonValidator` repair runs regardless). Safe by construction: removing a property is ignored on load of any existing config (STJ skips unknown keys); adversarial review confirmed zero dangling references (prod/test/docs/reflection), no FieldDefinition-order collision, and that the BUILD.md claim is accurate. *(If a structured-JSON escape hatch for a misbehaving gateway is ever wanted, it should be a deliberate visible+wired feature, not a hidden no-op.)* Found by the third A→Z convergence re-audit. (Sibling finding `CustomFilterPatterns` is the OPPOSITE case — its consumer (`RecommendationValidator`'s custom-patterns ctor) exists and is tested but the setting isn't plumbed to it, so that one is queued to be **wired**, not deleted.)

### Fixed (enrichment — MusicBrainz LRU cache key now matches the decoded query text — 2026-05-30)

- **`MusicBrainzResolver`'s in-process LRU cache key was built from the *raw* artist/album, but the query and local name-match use the `HtmlDecode`d text.** A residual of the `&`-resolution fix (which decoded the query, the match, and `ArtistMbidResolver`'s cache key — but missed *this* resolver's LRU key): a model-emitted `"Simon &amp; Garfunkel"` and a plain `"Simon & Garfunkel"` — the identical MusicBrainz entity — keyed differently, so the second was a cache **miss** and fired a redundant MusicBrainz query (and the resolved entry was stored under a raw-encoded key the decoded path would never look up). Now the key is built from the same `HtmlDecode`d names, so both encodings collapse to one key and one query. No correctness change to which MBID is returned (the album is part of the key, so distinct albums never collide); this is a redundant-query / cache-consistency fix. Adversarial review tried to construct a wrong-MBID collision and was refuted (the key includes the album, and `HtmlDecode` only merges encodings of the *same* text — it introduces no collision between distinct entities beyond what the existing alphanumeric normalization already does). Pinned by a test asserting the two encodings resolve to the same entity in a single network call. (Found by the third A→Z convergence re-audit; closes the heuristic-(s) gap the ampersand fix left behind.)

### Fixed (settings — Backfill Strategy label now matches the actual default — 2026-05-30)

- **The *Backfill Strategy* field advertised "Default: Aggressive" but the shipped default is Standard.** The label and help-text claimed Aggressive was the default while the constructor sets (and a test pins) `Standard` — so the UI promised "more passes, tries to guarantee target" while a fresh install actually ran the more conservative Standard profile. Corrected the label/help-text to mark **Standard** as the default (the shipped, tested behavior was kept — changing the runtime default would have silently increased every default user's provider-call volume). A new test pins the field's "(Default)" annotation to the actual constructor default so the two can't drift again. (Found by the second A→Z convergence re-audit.)

### Removed (packaging — stale legacy .lidarr.plugin discovery file — 2026-05-30)

- **Deleted the repo-root `.lidarr.plugin`**, a pre-3.0-era discovery file the current Lidarr host no longer reads (plugin discovery is by DLL glob + the host `Plugin` subclass; the host tree has zero references to it). It shipped in the release ZIP but the release version-stamp never rewrote it, so it was frozen at stale/misleading metadata — `version 1.0.3` (vs 1.6.1), `minimumVersion 2.13.1.4681` (vs `minHostVersion 3.0.0.4855`), and a `releaseUrl` pointing at a non-existent `…net6.0.zip`. Removed the file, its `manifest.json` `files[]` entry, and the package/validate references in `plugin-package.yml`. No install impact (the host ignores it; the Common release workflow copies it only `if [ -f ]`, and Brainarr's packaging policy requires only `Lidarr.Plugin.Brainarr.dll` + `plugin.json` + `manifest.json`). A `VersionContractTests` guard now fails if the file or a manifest entry for it returns. Closes the last of the three MEDs from the A→Z convergence re-audit. (Two related LOW metadata nits — `commonVersion` label vs Common's VERSION, and manifest `downloadUrl`'s unsubstituted `{version}` — remain queued.)

### Fixed (enrichment — "&"-in-name artists now resolve to MusicBrainz IDs — 2026-05-30)

- **Artists/albums with an ampersand (Simon & Garfunkel, Hall & Oates, Earth, Wind & Fire) were resolving to fewer MusicBrainz IDs.** The recommendation sanitizer HTML-encoded `&`→`&amp;` before the pipeline, so the MBID resolvers queried MusicBrainz with `…&amp;…` (escaped to `%26amp%3B`) and normalized the name to `…ampgarfunkel` vs MusicBrainz's `…garfunkel` — the match failed and, with *Require MusicBrainz IDs* on, those recommendations were dropped or queued. Fixed in two parts: (1) the sanitizer no longer HTML-encodes `&` (these names go to MusicBrainz query params via `Uri.EscapeDataString` and to Lidarr import as plain text — never rendered as HTML; the XSS/HTML-tag/SQL/path stripping still runs); (2) both MBID resolvers (`MusicBrainzResolver`, `ArtistMbidResolver`) defensively `HtmlDecode` the name before building the query, the local match, and the cache key, so even a model that emits a literal `&amp;` resolves correctly. This also aligns the in-memory `RecommendationHistory` dedup key with the decoded `DuplicationPrevention` key. Adversarial review confirmed no injection/XSS regression (every sink percent-escapes or is the host's own escaped DTO) and caught two spots where the decode was initially applied to the query but not the co-located match/cache-write — both fixed. Pinned by tests: the sanitizer preserves a raw `&` (not `&amp;`), and the resolver query carries `%26` (never `amp`) for both raw-`&` and model-emitted-`&amp;` inputs. (Found by the A→Z convergence re-audit.)

### Fixed (triage — Provider Calibration now actually affects triage decisions — 2026-05-30)

- **The `Enable Provider Calibration` setting was silently ignored at the triage decision sites.** It's meant to apply per-provider confidence calibration to triage scoring, but the three sites that act on triage — the review-queue UI chips, the dry-run simulation, and the **auto-apply** path that accepts items into Lidarr — all called the provider-less analyze overload, so they used raw, uncalibrated confidence regardless of the toggle (only the `review/explain` endpoint honored it). Same "read but not applied at the load-bearing site" class as the earlier confidence-provenance bug. Now all four triage paths run through one helper that applies calibration exactly when the setting is on (`EnableProviderCalibration ? settings.Provider : null`), so a low-quality local provider's scores are calibrated before the accept/review/reject decision. Adversarial review confirmed the change can't make a *bad* item auto-accept (calibration profiles have scale<1, so near-threshold confidence only decreases; the risk-reducer is bounded). Pinned by a test asserting calibration is applied when enabled and skipped when off, at the decision site. (Found by the A→Z convergence re-audit.)

### Fixed (security — Gemini API key leak in the model-dropdown path — 2026-05-30)

- **The Gemini model-list enumeration could leak your API key into the logs (HIGH)** — the same leak class fixed earlier in `BrainarrGeminiProvider`, but in the sibling `GeminiModelDiscovery` path that populates the model dropdown. The key rides in the URL query (`?key=…`), and the request didn't set `SuppressHttpError`, so a non-2xx response (an invalid or not-yet-activated key — a common first-run state when the dropdown auto-populates) made the host throw an `HttpException` whose URL-bearing message NLog rendered **unredacted** through the catch's `_logger.Debug(ex, …)`. Fixed by setting `request.SuppressHttpError = true` so non-2xx returns a response handled by the existing status-code branch (which logs only the status), eliminating the URL-bearing throw. Found by the A→Z convergence re-audit; pinned by guard tests (asserts `SuppressHttpError` is set and that a 400 degrades to the default model list without throwing). (Re-audit also queued 3 MED follow-ups: `EnableProviderCalibration` not applied at the triage decision sites, `&`→`&amp;` sanitizer encoding degrading MBID resolution, and a stale `.lidarr.plugin` shipping a `net6.0` URL.)

### Fixed (resilience — per-metric raw points are now size-capped — 2026-05-30)

- **`MetricsCollector`'s per-metric raw-point list is now bounded between retention sweeps.** Points were trimmed only by the hourly 24-hour-retention sweep, so a hot metric recorded in a burst could accumulate `24h × call-rate` points in memory before the next sweep. Each `MetricAggregator` now caps `_points` at 10,000, trimming to the newest ~90% on overflow (the windowed summaries read the most-recent points, so the oldest are dropped). Closes the last item from the unbounded-growth audit (#54). Also added the previously-missing eviction-cap tests for the already-bounded `ValidationMetrics` history (single + batch paths); the `TokenCostEstimator.UsageHistory` cap test is queued behind a small test-reset hook. With this, every long-lived in-memory collection in the plugin is verified bounded.

### Fixed (resilience — ExecuteWithCt no longer discards a completed response on a simultaneous cancel — 2026-05-30)

- **`HttpProviderClient.ExecuteWithCt` (the cancellation primitive wrapping Lidarr's token-less `IHttpClient`)** raced the HTTP call against the cancellation token via `Task.WhenAny`. If the response arrived and the token fired at nearly the same time, `WhenAny` could return the cancel task even though the HTTP task had already completed — throwing `OperationCanceledException` and discarding a perfectly good response. Now it honors a completed response/fault and only treats the outcome as cancelled when the response genuinely hasn't arrived. (This was the second timing-sensitive test in the intermittent-flake cluster; the test that surfaced it relied on a fast response beating a wall-clock timer, which broke under thread-pool starvation in the full parallel suite — rewritten to be deterministic.)

### Fixed (resilience — model-detection backend marked down despite a late timeout — 2026-05-30)

- **A connection-refused local backend (Ollama/LM Studio) could fail to be marked "down," causing redundant re-probes** (and an intermittent ~1/10 test flake under load). Model detection wraps its HTTP retries in an overall `ModelDetectionTimeout` cancellation; under load that timeout could cancel a *later* retry attempt, so the exception reaching `BackendHealthCache.MarkDown` was an `OperationCanceledException` rather than the earlier `SocketException`. Since `MarkDown` only records *connection-class* failures (it deliberately ignores timeouts, which may be a slow-but-alive backend), the cancellation masked the real connection failure and the backend was never marked down — so the next call re-probed instead of fast-failing within the grace window. Fixed by preserving the connection-class exception across all retry attempts (and preferring it in the outer catch) so `MarkDown` sees the `SocketException` even when a later attempt is cancelled; a pure timeout with no connection-class failure still does not mark the backend down. This was the previously-unidentified intermittent full-suite flake — root-caused and pinned by a deterministic test (attempt 1 = SocketException, later attempts = cancellation → backend still marked down → second call skips HTTP).

### Fixed (resilience — recommendation history is now pruned (was unbounded) — 2026-05-30)

- **`RecommendationHistory`'s suggestion/rejection tracking grew without bound** (in RAM and on disk) on the process-lifetime singleton — a `CleanupOldEntries()` with a 180-day retention existed but had **no caller**. It's now wired: pruned once on load (startup) and again on a 6-hour throttle at the top of each `RecordSuggestions` run, sharing that method's single write. Only `Suggestions`/`Rejected` are pruned (high-cardinality, time-stamped); `Accepted` (library-bounded) and `Disliked` (user-driven) are left intact. An adversarial review confirmed the 180-day retention is safely longer than every exclusion window that reads this data — `RecentlyRejected` is 30 days, and `OverSuggested` (count-based, no window) can't be reset out from under an actively-suggested artist because each re-suggestion refreshes `LastSuggested` and shields the entry from pruning. Pinned by load-time-prune (stale gone, recent kept) and recording-still-works tests. (Second of the two slower leaks from the #54 audit; `MetricsCollector._points` size-cap remains queued.)

### Fixed (resilience — recommendation-history dedup set no longer leaks memory — 2026-05-30)

- **The cross-session dedup set grew without bound on a long-running Lidarr process.** `DuplicationPreventionService._historicalRecommendations` (the in-memory set that stops the same `artist|album` being recommended across runs) was an unbounded `HashSet<string>` on a process-lifetime singleton — one entry per unique recommendation ever made, never evicted (`ClearHistory()` has no production caller). Over weeks of hourly syncs that's a slow heap leak. It's now a capped `Dictionary<string,DateTime>` (default 50,000 entries) that evicts the oldest down to ~90% in a single pass when over the cap — guaranteeing the bound for any batch size, mirroring `MusicBrainzResolver`'s LRU. Dedup semantics are byte-for-byte preserved (keys are pre-normalized/lowercased; ordinal comparison matches the old default-comparer `HashSet`), and the on-disk `RecommendationHistory` still backstops long-term dedup for anything evicted. Pinned by bounded-across-many-batches, single-over-cap-batch, and dedup-still-works tests. (An audit of all long-lived collections found everything else already bounded — LimiterRegistry, the resolver caches, circuit breakers, model registry, plan cache — and queued two slower MED leaks: `RecommendationHistory`'s dead `CleanupOldEntries`, and `MetricsCollector._points` size-cap-between-sweeps.)

### Added (observability — confidence-floor starvation is now visible — 2026-05-30)

- **You can now see when the Minimum Confidence floor is what's keeping a run under target.** The safety gate logs a per-run, reason-segregated summary — e.g. `Safety gate: 12 passed, 6 below Minimum Confidence 0.80, 2 missing required MBID(s) (8 queued for review)` — and the run-summary "Under target" line now names the floor specifically when it gated items this run (`N recommendation(s) were held below your Minimum Confidence floor (0.80) this run — lower it to include them`), instead of only listing generic typical causes. The floor count is **provenance-aware**: score-less items (model gave no confidence) bypass the floor and are never counted, and an MBID-gate drop is not misattributed to confidence. The per-run count flows through an async-scoped accumulator (`GateMetricsContext`), not a process-wide counter — so concurrent fetches (e.g. a Test action overlapping a scheduled sync) can't inflate each other's hint (a cumulative-counter delta would have raced on the shared metrics singleton). (The companion "kill misleading `Model=default` logs" item was investigated and found already-clean — every remaining `"default"` is a legitimate missing-segment label fallback, pricing-table key, or the Z.AI sentinel that's resolved before send — so no change was needed there.)

### Fixed (resilience — run cancellation no longer swallowed in the top-up/enrichment paths — 2026-05-30)

- **A cancelled recommendation run is now propagated instead of being silently turned into a partial "success".** On the cancellation-aware fetch path, `OperationCanceledException` from a cancelled *run* token was being swallowed in three places and the partial results returned as if the run completed — defeating the orchestrator's `catch (OperationCanceledException) → return empty` handler and mislabelling a cancel/timeout as normal completion:
  - `IterativeRecommendationStrategy` — the per-iteration `catch (Exception)` logged a cancelled run as a misleading "Iteration N failed" ERROR and returned the partial list.
  - `TopUpPlanner` — its broad catch (the *sole* caller of the strategy) **re-swallowed** the propagated cancel one frame up (caught by an adversarial review — fixing only the strategy would have been a no-op).
  - `MusicBrainzResolver` / `ArtistMbidResolver` — the enrichment loops swallowed mid-request cancellation and logged it as a per-item "resolution error".
  All four now propagate via `catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }` (and `ThrowIfCancellationRequested()` at the enrichment loop heads). Crucially, a provider's **own** request timeout — which surfaces as `OperationCanceled`/`TaskCanceled` while the run token is *not* cancelled — still falls through to the recoverable per-iteration path (break + return what was collected), so a single slow provider call doesn't abort the whole run. **Scope note:** the shipped `Fetch()` entry point runs the sync path (`RunSafeSync`, which enforces the overall budget via a wall-clock `Task.Wait` and threads a `None` token), so this is currently a correctness-hardening fix for the cancellation-aware/host-abort path rather than a change to today's production behavior. Pinned by strategy-, TopUpPlanner-seam-, and resolver-level tests (cancel propagates; provider-own-timeout returns partial).

### Fixed (tests — release-installability E2E skips on download timeout — 2026-05-30)

- **`PublishedReleaseInstallabilityTests.LatestPublishedRelease_ZipContents_Match_PackagingPolicy` no longer fails the suite on a transient network blip.** The class documents "skips gracefully when GitHub is unreachable" and guards the releases-*list* fetch (`TryGetReleasesAsync` → null → `Skip.If`), but the multi-MB asset **download** was unguarded — a 30s `HttpClient.Timeout` surfaced as a hard `TaskCanceledException` failure. Wrapped the download so any `HttpRequestException`/`TaskCanceledException`/`TimeoutException`/`IOException` becomes a `SkipException`, matching the documented contract. (It's `[Trait("Category","ReleaseE2E")]`, intended to be filtered from the default sweep anyway.)

### Fixed (tests — quarantined sandbox tests now skip deterministically — 2026-05-30)

- **The 8 quarantined `PluginSandboxRuntimeTests` no longer fail the suite when the merged plugin DLL is present.** Brainarr is ImportList-only by design and ships no concrete Common-`IPlugin` class, so `PluginSandbox` (which reflects for one) throws `InvalidOperationException: …does not contain a concrete IPlugin implementation`. The tests were `[Trait("State","Quarantined")]` but their skip was keyed only on DLL *absence* (`FindPluginDll()` throwing `SkipException`), so once `Brainarr.Plugin/bin/Lidarr.Plugin.Brainarr.dll` existed (e.g. after any local build) they ran and **failed** instead of skipping — 8 deterministic failures that masked the suite's true health. Wrapped sandbox creation in a helper that converts that specific exception into a `SkipException`, so they skip whether or not the DLL is built. Full suite now 3139 passed / 0 failed / 17 skipped. (Reminder for future runs: capture `dotnet test`'s own exit code — piping through `grep`/`tail` reports the pipeline's exit, not the test run's, which had hidden these failures.)

### Fixed (review-queue triage — confidence provenance — 2026-05-30)

- **The review-queue triage advisor no longer mislabels recommendations the AI never scored.** Many models omit a confidence score, so the parser fabricates a placeholder (0.7). The triage advisor used to treat that placeholder as a real score — flagging the item `CONFIDENCE_BELOW_THRESHOLD` (+2 risk) whenever you raised *Minimum Confidence* above 0.7, painting it with a fabricated `high`/`medium`/`low` band, and running it through provider calibration — i.e. the same fabricated-confidence cliff the safety gate already closed, leaking into triage. Recommendations now carry `ConfidenceProvided` all the way into the review queue (`ReviewItem` gained the field; `Enqueue` and the dequeue rebuild both copy it). When confidence wasn't model-reported, triage **skips all confidence-derived scoring** (calibration, below-threshold penalties, the high-confidence-with-MBID risk reducer), labels the band `unscored`, and adds an explicit `CONFIDENCE_NOT_PROVIDED` reason — while still enforcing the confidence-independent checks (required MBIDs, duplicate signals). This makes triage consistent with `SafetyGateService` (which already declines to drop score-less items on the floor). **Behavioral note:** a score-less item that fails *only* the MBID gate now lands at `accept`/`review` rather than being pushed to `review`/`reject` by a raised floor — so with **Auto-Apply Triage Actions** enabled (default off) such items become eligible for auto-accept; they remain clearly marked `unscored` / `CONFIDENCE_NOT_PROVIDED` in the queue UI and audit log. Back-compat: `review_queue.json` entries written before the field existed deserialize as `provided=true` (System.Text.Json honors the property initializer), preserving their prior triage. Follow-ups queued: promote `CONFIDENCE_NOT_PROVIDED` to Common's canonical `TriageReasonCodes`; decide wire-or-delete for the unused `MinimalResponseParser`.

### Fixed (Gemini API-key log leak — 2026-05-30)

- **The Gemini provider could leak your API key into the logs on any failed request (HIGH).** Gemini carries the key in the URL query string (`?key=AIza…`, the only auth scheme Google's `generateContent` endpoint accepts). On a non-2xx response the host `IHttpClient` threw an `HttpException` whose message embeds the full request URL; that exception was chained as the `InnerException` of the mapped provider error and rendered **unredacted** by NLog's exception renderer — the existing `LogRedactor` only scrubs the message string, never the exception object, and no target-level scrubber is registered. So every 401/403/429/5xx wrote the key to disk. Fixed by setting `request.SuppressHttpError = true`, which routes all non-2xx responses through the status-code branches already present in `CompleteAsync`/`CheckHealthAsync` (they map with `inner: null`, so no URL-bearing exception is ever constructed). Two regression tests pin it: one asserts `SuppressHttpError` is set on the outgoing request, one asserts a 401 produces no inner exception and the key never appears in the error's `ToString()`. (Sibling note: the Anthropic provider uses the *same* `hex`-as-inner pattern but is **not** affected — its key is in the `x-api-key` header, and the host exception renders only the URL, not headers.)

### Tests / audit (Anthropic + Gemini providers — 2026-05-30)

- **Contract-swept the two distinct-format providers** (Anthropic Messages API, Gemini `generateContent`) — provider-matrix pass #5, completing the matrix. Both verified sound on the core contract: Anthropic uses `x-api-key` + `anthropic-version` (correct for `sk-ant-` keys — *not* the OAuth bug), system at top level, null-safe `content[].text` parse; Gemini uses `contents/parts`, gates `responseMimeType=application/json` on `JsonMode`, and is fully null-safe on SAFETY/MAX_TOKENS/empty-candidate responses (no parse NRE). **Both flow `max_tokens`/`maxOutputTokens` from the timeout-aware `request.MaxTokens` budget — neither hardcodes it.** Closed the MED coverage gap (same class as the mainstream sweep): `CompleteAsync` auth + endpoint were unpinned in *both* test files — added `CompleteAsync_UsesXApiKeyAndVersion_AgainstMessagesEndpoint` (Anthropic, also asserts **no** `Authorization` header, guarding the OAuth providers from regressing into x-api-key) and `CompleteAsync_PinsEndpointKeyInQuery_AndSuppressesHttpError` (Gemini). Fixed the one HIGH finding (Gemini key-in-URL log leak — see above). LOW findings queued: Gemini sends the system prompt concatenated into the user part rather than the native `systemInstruction` field; Anthropic `temperature` is unclamped (in range for all current call sites).

### Changed (settings — 2026-05-30)

- **`Minimum Confidence` is now a visible advanced setting** (was hidden). The confidence floor — recommendations the AI scores below it are dropped, or queued when *Queue Borderline Items* is on — is now adjustable from the UI with help text and `0.0–1.0` validation (out-of-range is rejected with a clear message instead of silently clamped). The help text warns about a non-obvious cliff: many models don't report a confidence score, so the parser defaults those to `0.7`; raising the floor above `0.7` therefore filters out every recommendation that lacks an explicit score (a deeper fix to the fabricated-score behavior is queued).

### Tests / audit (mainstream providers — 2026-05-30)

- **Contract-swept the five mainstream OpenAI-compatible providers** (OpenAI, OpenRouter, DeepSeek, Groq, Perplexity) + the generic `OpenAi-Compatible` provider — all verified sound: `Authorization: Bearer` (no x-api-key/OAuth confusion), correct per-provider endpoints, `max_tokens` flows from the timeout-aware `request.MaxTokens` budget (none hardcode it), null-safe JSON parsing, accurate capability flags, and OpenRouter correctly sends its required `HTTP-Referer`/`X-Title` headers. Closed the one finding (MED — a coverage gap, not a live bug): the `CompleteAsync` auth-header + endpoint were only pinned for OpenRouter/OpenAi-Compatible, leaving OpenAI/DeepSeek/Groq/Perplexity unpinned on the path the pipeline actually uses — exactly the gap that hid the Claude subscription `x-api-key` bug. Added `CompleteAsync_UsesBearerAuth_Against…Endpoint` guard tests to all four. (LOW findings — 6-way logic duplication, OpenRouter bare health-probe model id — queued.)

### Changed (OpenAI Codex subscription — 2026-05-30)

- **Audited the OpenAI Codex (Subscription) provider; clarified a known limitation instead of a risky blind fix.** Its auth scheme is correct (`Authorization: Bearer`, the right header for OpenAI — both API keys and OAuth use Bearer). It works when `~/.codex/auth.json` contains an `OPENAI_API_KEY`. But a *pure* ChatGPT-subscription OAuth token (`tokens.access_token` from `codex auth login`, no API key) does **not** authenticate against the public `api.openai.com/v1/chat/completions` endpoint — the codex CLI uses a separate ChatGPT backend (Responses API + `chatgpt-account-id` header) this provider doesn't yet speak. Unlike the Claude fix (a documented endpoint → bounded header change), the codex backend is undocumented/volatile, so an adversarial review judged a blind rewrite ~50%+ likely to still fail and able to regress the working API-key path. Instead: the 401 hint now routes OAuth users to the working `OPENAI_API_KEY` path, the provider doc no longer overstates pure-OAuth support, and the auth/endpoint contract is pinned by a new test. Real ChatGPT-backend support is queued for a Codex subscriber to live-verify.

### Fixed (Claude Code subscription auth — 2026-05-30)

- **Claude Code (Subscription) provider could never authenticate.** It sent the Claude Pro/Max **OAuth access token** (`claudeAiOauth.accessToken` from `claude login`) via the `x-api-key` header — but Anthropic authenticates OAuth tokens via `Authorization: Bearer` + an `anthropic-beta: oauth-2025-04-20` opt-in flag; `x-api-key` is only for `sk-ant-` API keys. So every subscription sync got HTTP 401. The provider had used `x-api-key` since it was introduced (PR #310), with no test pinning the auth header. Fixed to `Authorization: Bearer` + the oauth beta flag (the sibling Z.AI Coding provider, which emulates Claude Code, uses the same Bearer scheme — live-confirmed). A header-assertion regression test now pins it. **Needs verification by a Pro/Max subscriber** (not live-testable in CI): if it still 401s, the dated beta value may have rotated and/or the endpoint also requires the `claude-cli` User-Agent (which needs a raw-HttpClient migration — queued, since Lidarr's dispatcher forbids non-Lidarr UAs).

### Tests / audit (Z.AI GLM — 2026-05-30)

- **`BrainarrZaiGlmProvider` audited end-to-end and verified sound** (provider-matrix pass #1). Unlike its sibling `BrainarrZaiCodingProvider`, the OpenAI-format PaaS endpoint correctly *sends* `temperature` (the `[1210]` rejection is Anthropic-Coding-specific), uses the timeout-aware `max_tokens` budget, and maps `1113`/`1115` → QuotaExceeded with a switch-to-Coding hint. Added contract-guard tests locking in the **opposite-temperature** sibling contract (`CompleteAsync_SendsTemperature`) and `max_tokens` passthrough/default, so the two providers can't be wrongly unified. An adversarial audit refuted a suspected garbage-in-from-error-envelope path (yields empty, never bad recs) and found only two LOW/dormant asymmetries on the unused `StreamAsync` path (queued).

### Fixed (confidence floor — 2026-05-30)

- **The confidence floor no longer silently zeroes out providers that omit confidence scores.** Parsers fabricate a default score (0.85) when the model omits one; raising `Minimum Confidence` above that default used to drop *every* score-less recommendation. Recommendations now carry `ConfidenceProvided` — the floor only filters items the model *explicitly* scored below it; items with no model score are kept. **Crucially, the flag is preserved through the sanitizer and MBID-enrichment rebuilds** (the MBID resolvers were converted to record `with`-copies so unchanged fields can't be dropped) — an adversarial review caught that an earlier version reset the flag in those rebuild sites, making the fix a no-op in the real pipeline. Live-verified: a sync still validates 30/30 and resolves MBIDs end-to-end. Follow-ups queued: triage-advisor + review-queue provenance awareness.

### Fixed (tests — 2026-05-30)

- **`LimiterRegistryBounded` full-suite flake eliminated at the root.** The collection name was used by three test classes but had no `[CollectionDefinition]`, so it ran in parallel with collections that mutate `LimiterRegistry`'s process-wide static dictionaries — racing the bounded-dict assertion (passed isolated, flaked ~1/3 full runs). Added `[CollectionDefinition("LimiterRegistryBounded", DisableParallelization = true)]` to serialize all LimiterRegistry-static-state tests (same mechanism `OrchestratorIntegration` uses), which also fixes the latent same-cause flake in `LimiterRegistryMaintenanceTests`. Restored the strong clear-then-insert assertion + added a race-immune bound check. Green across 8 consecutive full-suite runs.

### Added (recommendation engine + style-seeded discovery — 2026-05-29)

- **Discovery-mode escalation on dedup saturation.** During top-up, when iterations stop producing new artists (the library/history dedup keeps rejecting the same cluster) and you're still under target, Brainarr now *widens* the effective discovery mode one step toward Exploratory (Similar→Adjacent→Exploratory) and keeps going, instead of giving up. Live-confirmed: a lo-fi run saturated at iterations 3–4 (0% unique), escalated Adjacent→Exploratory, and iteration 5 broke out with fresh artists — lifting the result from ~25 to **33/50**. Gated to the aggressive/top-up path (where filling the target is the goal), bounded by a hard iteration ceiling, and the original `DiscoveryMode` setting is never persisted-over. (`IterativeRecommendationStrategy.TryEscalateDiscoveryMode`)
- **Search by music style — even styles your library doesn't contain.** Selecting (or free-typing) styles in **Music Styles** now seeds *genre-first* discovery: when your library has zero coverage of the chosen styles, Brainarr recommends the defining artists OF those styles instead of trying (and failing) to tie them to your existing collection. Live-confirmed: with a non-lo-fi library and "lo-fi" selected, it returned Nujabes, J Dilla, MF DOOM, Tomppabeats, Birocratic, Tokimonsta, Joji, Kiefer, … (`LibraryPromptRenderer` genre-first prompt; `RecommendationPipeline.IsStyleSeededDiscovery` skips the library-consistency post-filter in this mode; both gate on the *same* `StyleContext.StyleCoverage==0` signal so they never disagree). The 85-entry catalog dropdown already existed; **freestyle text** (styles not in the catalog, e.g. "vaporwave") now passes through as a seed anchor instead of being silently dropped (`DefaultStyleSelectionService`).
- **`AI Request Timeout` now actually governs the whole run.** The overall recommendation fetch budget is derived from `AIRequestTimeoutSeconds × (1 + top-up iterations) + overhead` (`BrainarrSettings.GetOverallFetchTimeoutMs`), not a hardcoded 120s. Previously a raised timeout (e.g. 360s for slow GLM-5.x reasoning models) was silently guillotined at 2 minutes mid-top-up, capping results. Floored at the legacy 120s, capped at 30min, and it mirrors the local-provider (Ollama/LM Studio) timeout elevation so a single local request is never starved.
- **`max_tokens` scales to the target count** instead of a flat 2000 (`GetOutputTokenBudget`), but is bounded by what the model can generate within the per-request timeout — overshooting just cancels the call mid-stream (nothing to salvage). So a larger list completes in one request when you grant the time, and short timeouts still floor at the proven-safe 2000. (This is the *output/completion* cap — unrelated to the model's much larger input context window.)
- **Run summary reports target attainment** (`items/target` + %) distinctly from the provider-health success rate (now labeled `providerSuccess`), plus an under-target explainer naming the likely cause (timeout / dedup / gating) — ending the "100% success but 17/50 delivered" confusion.

## [1.6.1] - 2026-05-29

### Fixed

- Invariant casing in genre-overview matching (Turkish-I locale bug).
- Memory caps for `ValidationMetrics` history and `ArtistMbidResolver` MBID cache (`BoundedConcurrentDictionary`).
- Retry-After header parsing now uses Common's `LlmErrorMapper.ParseRetryAfterHeader`.
- Hardened recommendation pipeline against untrusted LLM data.

### Changed

- Performance: single thread-pool hop for `ReviewQueueService.Enqueue` batch.
- CI: repoint Common reusable workflow refs to `@workflows/v1`.
- `ext/Lidarr.Plugin.Common` submodule bumped to `594a73b`.

### Removed

- Dead code: `ServiceResult<T>`, `NLogWrapper`, `DateUtil` utility class.

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.6.0...v1.6.1)

## [1.6.0] - 2026-05-28

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

[Full diff](https://github.com/RicherTunes/Brainarr/compare/v1.5.6...v1.6.0)

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

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/v1.6.1...main
[1.6.1]: https://github.com/RicherTunes/Brainarr/compare/v1.6.0...v1.6.1
[1.6.0]: https://github.com/RicherTunes/Brainarr/compare/v1.5.6...v1.6.0
[1.5.6]: https://github.com/RicherTunes/Brainarr/compare/v1.5.5...v1.5.6
[1.5.5]: https://github.com/RicherTunes/Brainarr/compare/v1.5.4...v1.5.5
[1.5.4]: https://github.com/RicherTunes/Brainarr/compare/v1.5.3...v1.5.4
[1.5.3]: https://github.com/RicherTunes/Brainarr/compare/v1.4.0...v1.5.3
[1.4.0]: https://github.com/RicherTunes/Brainarr/compare/v1.3.2...v1.4.0
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
