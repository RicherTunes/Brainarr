# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

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
