# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Brainarr is a **production-ready** multi-provider AI-powered import list plugin for Lidarr that generates intelligent music recommendations. The project supports 11 different AI providers ranging from privacy-focused local models to powerful cloud services (including 2 subscription providers that reuse existing CLI credentials).

## Runtime & Docker Image Requirements (CRITICAL)

**Target framework**: `net8.0` — all plugins MUST target .NET 8.

**Lidarr Docker image**: Use ONLY a `.NET 8` plugins-branch image. The current pinned tag is:

```text
LIDARR_DOCKER_VERSION=pr-plugins-3.1.2.4913
```

- Image: `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913`
- Digest: `sha256:ae0b3b14769fdfeb73fe5d9e61ebcda04edf202244bcbd6323d2fe1381154f57`
- Pinned in: `.github/lidarr_digest.txt` and `scripts/extract-lidarr-assemblies.sh`

**NEVER use `pr-plugins-2.x` tags** — those are .NET 6 images. Loading a .NET 8 plugin into a .NET 6 host causes `System.Runtime` assembly load failures and Lidarr crash-loops. The guardrail in `extract-lidarr-assemblies.sh` will catch this (fails if `System.Runtime.dll` major != 8).

When bumping the Docker image tag, update ALL of these locations:
- All `.github/workflows/*.yml` files referencing `LIDARR_DOCKER_VERSION`
- `.github/lidarr_digest.txt`
- `scripts/extract-lidarr-assemblies.sh` (default fallback)
- `scripts/snapshots/run-local.sh` and `run-local.ps1`
- `test-local-ci.sh`

## Plugin DLL Naming Contract (CRITICAL)

**The main plugin DLL filename MUST match the glob `Lidarr.Plugin.*.dll`.** Lidarr's PluginLoader (`NzbDrone.Common/Extensions/PathExtensions.cs:334`) scans `/config/plugins/{owner}/{name}/` with `Directory.GetFiles(folder, "Lidarr.Plugin.*.dll")` — any other filename is silently ignored. No error, no warning, no log line; the plugin just never appears in `/api/v1/system/plugins`.

For Brainarr this is satisfied by `<AssemblyName>Lidarr.Plugin.Brainarr</AssemblyName>` in `Brainarr.Plugin/Brainarr.Plugin.csproj`. Don't drop that line "to clean up" — it's load-bearing.

## Plugin Packaging Policy (CRITICAL)

**The plugin package MUST contain:**
- `Lidarr.Plugin.Brainarr.dll` - Main plugin
- `Lidarr.Plugin.Abstractions.dll` - Required for plugin discovery (host does not ship it)
- `plugin.json`
- `manifest.json`

**The plugin package MUST NOT contain host-provided contract assemblies (type-identity conflicts):**
- `FluentValidation.dll`
- `Microsoft.Extensions.DependencyInjection.Abstractions.dll`
- `Microsoft.Extensions.Logging.Abstractions.dll`
- `NLog.dll`
- `System.Text.Json.dll`
- `Lidarr.*.dll` (non-plugin host assemblies)
- `NzbDrone.*.dll`

**The plugin must reference host versions of contract packages.** Use the host-version coupling tests and `scripts/check-host-versions.ps1` to keep NuGet versions aligned with the Lidarr host.

**Do not rely on copying host-provided contract assemblies into the plugin output:**

```xml
<!-- OK for compile-time (runtime resolves from host); packaging MUST exclude the DLL -->
<PackageReference Include="FluentValidation" />
```

**Validation:** Run `./build.ps1 -Package` and verify the zip contains the required DLLs.

## Plugin Registration (CRITICAL — controls Lidarr System→Plugins UI visibility)

Lidarr has **two** distinct `IPlugin` interfaces, and conflating them silently breaks the System→Plugins UI:

| Interface | From | Used by |
|---|---|---|
| `NzbDrone.Core.Plugins.IPlugin` | `Lidarr.Core.dll` (host) | `/api/v1/system/plugins` — UI listing, update checks, uninstall |
| `Lidarr.Plugin.Abstractions.IPlugin` | Common (internalized via ILRepack) | TestKit `PluginSandbox` — never read by the live host |

`BrainarrPluginHost : StreamingPlugin<TModule,TSettings>` satisfies Common's contract for the bridge. It does **not** satisfy the host's `IPlugin`, so without an additional class the plugin loads fine and exposes `/api/v1/importlist/schema` but doesn't appear in System→Plugins (and can't be auto-updated/uninstalled through the UI).

`Brainarr.Plugin/Hosting/BrainarrInstalledPlugin.cs` extends the host's `NzbDrone.Core.Plugins.Plugin` to close the gap:

```csharp
public sealed class BrainarrInstalledPlugin : NzbDrone.Core.Plugins.Plugin
{
    public override string Name => "Brainarr";
    public override string Owner => "RicherTunes";
    public override string GithubUrl => "https://github.com/RicherTunes/Brainarr";
}
```

DryIoc's `RegisterMany` (in `NzbDrone.Common.Composition.Extensions.AutoAddServices`) auto-discovers this class from the loaded plugin assembly. `InstalledVersion` is derived from `AssemblyInformationalVersionAttribute` via the base class — do **not** hardcode it.

**Regression guard**: `Brainarr.Tests/Runtime/DockerE2ETests.cs::Plugin_AppearsInInstalledPluginsApi` asserts that `/api/v1/system/plugins` lists a "Brainarr" entry. Only a real Lidarr host can validate the type-identity match.

## Release Asset Naming (CRITICAL — controls Lidarr UI install)

**Every release asset filename MUST contain the literal substring `net8.0.zip`.**

Lidarr's plugin install flow (UI "Install" on a GitHub URL) is implemented in `src/NzbDrone.Core/Plugins/PluginService.cs` on the `plugins` branch. The asset filter is:

```csharp
release.Assets.Any(a => a.Name.Contains($"{Framework}.zip", StringComparison.OrdinalIgnoreCase))
// where Framework = $"net{_platformInfo.Version.Major}.0"  →  "net8.0"
```

If no asset matches, `GetRemotePlugin` returns `null` and `InstallPluginService.Execute` silently no-ops — **the UI spinner spins forever with no error message**. This is the failure mode users see as "Install button does nothing."

Other constraints the install enforces:

- `draft: false`
- `target_commitish` ∈ `{main, master}` (case-insensitive)
- Tag parses as a version (`v1.2.3`, `1.2.3`, or `1.2.3-prerelease`)
- Optional `Minimum Lidarr Version: X.Y.Z.W` in release body must be ≤ host version

Our release zip is named `Brainarr-${VERSION}.net8.0.zip` (release.yml) and `Brainarr-latest.net8.0.zip` (plugin-package.yml). **Do not rename these without keeping the `net8.0.zip` suffix.**

**Verify a release is installable:**

```bash
gh api repos/RicherTunes/Brainarr/releases --jq '.[0] | {tag_name, draft, target_commitish, assets: [.assets[].name]}'
```

At least one asset name must contain `net8.0.zip`.

## Release versioning: bump VERSION before tagging (CRITICAL)

The plugin's assembly version comes **only** from the repo-root `VERSION` file: `Brainarr.Plugin.csproj` sets `<GenerateAssemblyInfo>true</GenerateAssemblyInfo>` with no `<AssemblyVersion>` literal, and `Directory.Build.props` derives `VersionPrefix`/`AssemblyVersion` from `VERSION`. The release pipeline (`.github/workflows/release.yml` → the shared reusable `release-plugin.yml@workflows/v1`) stamps `plugin.json` and `manifest.json` from the **git tag**, and stamps any csproj `<AssemblyVersion>` tag — but brainarr's csproj has none (only a comment), and **the reusable workflow does NOT stamp the `VERSION` file**. So if you tag a version that differs from the committed `VERSION`, the build produces an assembly with the *old* version while `plugin.json` shows the *new* one → `/api/v1/system/plugins` reports the wrong `installedVersion` (the 1.3.2-vs-1.4.1 bug `VersionContractTests` exists to prevent).

**Release procedure:** bump `VERSION` **and** `plugin.json` **and** `manifest.json` to the same value, commit (CI's `VersionContractTests` enforce they agree), then tag `v<that-version>`. A `verify-version` pre-flight job in `release.yml` (gates the `release` job via `needs:`) fails the release early if the tag's version ≠ committed `VERSION`/`plugin.json`/`manifest.json`, so a mismatched tag can't ship. (`plugin-package.yml` builds committed sources and never stamps, so it can't drift.)

## Submodule pin coordination (ext-common-sha.txt)

`ext/Lidarr.Plugin.Common` is a git submodule pinned to a specific Common SHA. Two things must always agree on that SHA:

1. **The submodule gitlink** — what `git ls-tree HEAD ext/Lidarr.Plugin.Common` reports (updated by `git add ext/Lidarr.Plugin.Common` after checking out a new Common commit).
2. **`ext-common-sha.txt`** — a plaintext sentinel (40 hex chars + LF) at the repo root. CI's "Submodule Pinning" / `verify-pins` jobs fail the build if the gitlink and this file disagree.

**Why the sentinel exists**: the gitlink is invisible in a plain `git diff` (it shows only `-Subproject commit <sha>`), so the sentinel makes the pinned version greppable, reviewable in PRs, and assertable in tests (`VersionContractTests` cross-checks it against `plugin.json` / `manifest.json` `commonVersion`). Seeing `ext-common-sha.txt` dirtied in `git status` after a submodule bump is expected — commit it together with the gitlink.

**To bump the pin**: `pwsh ext/Lidarr.Plugin.Common/scripts/repin-common-submodule.sh --sha-from-submodule --stage` (or the `.ps1` variant) reads the submodule HEAD, rewrites `ext-common-sha.txt`, and stages both so they can't drift. The nightly `bump-common.yml` workflow does this automatically when Common's main advances.

## Common helpers in use

- `PluginConfigRoots.Resolve("Brainarr")` — `Brainarr.Plugin/Services/Support/ReviewQueueService.cs:26`, `Brainarr.Plugin/Services/Support/RecommendationHistory.cs:28`
- **Unused utility surface (no production consumer; kept intentionally, not dead-code to remove):** `Services/Core/ConcurrentCache.cs` (generic LRU+TTL cache — the live recommendation cache is `RecommendationCache`; `ConcurrentCache` is exercised only by its own tests + the quarantined cache stress tests) and `Services/Providers/Shared/FormatPreferenceCache.cs` (file-backed provider-format-preference store — has tests but nothing calls it; structured-JSON is decided by provider capability flags, see the deleted `PreferStructuredJsonForChat`). Both are well-formed/tested generic utilities deliberately retained; don't re-flag them as dead config in audits, and don't cite them as active "in use" examples.
- `BackendHealthCache` — `Brainarr.Plugin/Services/Providers/Llm/BrainarrOllamaProvider.cs:59`, `Brainarr.Plugin/Services/Providers/Llm/BrainarrLmStudioProvider.cs:64`, `Brainarr.Plugin/Services/ModelDetectionService.cs:41`
- `JsonFileStore<TKey, TValue>` — `Brainarr.Plugin/Services/Support/ReviewQueueService.cs:21` (`ReviewItem` store), `Brainarr.Plugin/Services/Support/ReviewActionAuditService.cs:36`
- `PluginLifecycle` — `Brainarr.Plugin/Hosting/BrainarrModule.cs:65` (`RegisterShutdown` for MetricsCollector + LimiterRegistry), `Brainarr.Plugin/Hosting/BrainarrModule.cs:78` (`Shutdown`)
- `WarnOnce` — `Brainarr.Plugin/Services/Tokenization/ITokenizer.cs` (tokenizer fallback gate). The `_fallbackWarn` field is **`static`** (process/ALC-wide) on purpose — Lidarr rebuilds `BrainarrImportList`'s per-instance DI `ServiceProvider` (and thus a fresh `ModelTokenizerRegistry`) per operation, so an instance-scoped gate re-fired the "no tokenizer registered" WARN every run. This matches `WarnOnce`'s documented `private static readonly` usage; **do not** revert it to an instance field. Test isolation via `ResetFallbackWarnStateForTests()`.
- `BoundedConcurrentDictionary<TKey, TValue>` — `Brainarr.Plugin/Services/Resilience/LimiterRegistry.cs:41-45` (5 static dicts capped at `DictCap` = 5120: `_semaphores`, `_throttledSemaphores`, `_overrides`, `_throttleUntil`, `_throttleCaps`), `Brainarr.Plugin/Services/Telemetry/MetricsCollector.cs:25` (`Metrics` capped at `MetricsCap` = 1024). Common v1.15.0 added the richer API surface (indexer setter, `ContainsKey`, `Values`, `IEnumerable<KeyValuePair>`) that lets us drop the hand-rolled `EnforceDictCaps()` / `EnforceMetricsCap()` helpers.
- `PathTraversalGuard.ContainsTraversalAttempt` — `Brainarr.Plugin/Services/Security/SecureUrlValidator.cs:96,151` (replaces local predicate; Common's separator-adjacency rule avoids `foo..bar` false positives)
- `TestValidationBuilder` — `Brainarr.Plugin/Services/Core/ConfigurationValidator.cs:Validate` (per-provider credential / URL field requirements gate the behavioral connection probe). Maps `AIProvider.Ollama → OllamaUrl`, `AIProvider.LMStudio → LMStudioUrl`, 8 cloud providers → their respective `*ApiKey` (Perplexity, OpenAI, Anthropic, OpenRouter, DeepSeek, Gemini, Groq, Z.AI), 2 subscription providers → their credentials-file paths, and treats `ClaudeCodeCli` as N/A (binary auto-detected from PATH). Per-field hints point the user at where to get the credential. Closes the parity-matrix `TestValidationBuilder MISSING` axis.

### `LlmAuthCircuit` — facade over Common's gate stack (wave-22 convergence)

- **`LlmAuthCircuit`** — `Brainarr.Plugin/Services/Resilience/LlmAuthCircuit.cs`. The wave-21 architectural divergence has been resolved: `LlmAuthCircuit` is now a thin facade over Common v1.16.0+'s `AuthFailureGate` + `SlidingWindowAuthFailureHandler` (`Lidarr.Plugin.Common.Services.Bridge`). Same documented public API (`IsOpen` / `RecordAuthFailure` / `RecordSuccess` / `MakeKey`) and the same documented behavior; the per-key state machine is the shared ecosystem implementation.
  - **Key hashing preserved**: `MakeKey` continues to produce `{providerId}::{SHA256(apiKey)[0..16]}` so raw LLM API keys never appear as dict keys — eliminates the heap-dump / debugger-inspection leak vector that matters more for LLM keys (broad permissions, billing-attached) than for streaming-session tokens.
  - **Sliding-window semantics live in Common now**: `SlidingWindowAuthFailureHandler(failureThreshold, failureWindow, clock)` handles the "K failures within W → latch" + "stale-failure forgetting" logic. Brainarr's `LlmAuthCircuit` ctor passes `failureThreshold=3, failureWindow=5min`.
  - **Open-duration semantics layered locally**: `LlmAuthCircuit` tracks `LatchedAt` per gate entry and refuses probe slots until `openDuration` (default 30 min) has elapsed since the latch. Common's `AuthFailureGate.TryAcquireProbeSlot` grants the FIRST probe immediately on first call after latch, but brainarr's documented behavior is "stay Open for openDuration before any probe" — this is the brainarr-side layer above the shared gate. Any failure while latched refreshes `LatchedAt` so a HalfOpen probe failure correctly resets the 30-min timer.
  - **Coverage**: `LlmAuthCircuit` is wired in **all 11 cloud / subscription providers** as of wave-22 Phase D: OpenAI, Anthropic, ClaudeCodeSub, Perplexity, OpenRouter, DeepSeek, Groq, Gemini, Z.AI GLM, Z.AI Coding, OpenAI Codex Subscription. Subscription providers key the circuit on their credentials-file path (closest stable identity since the bearer is loaded per-call from disk). Local providers (Ollama, LM Studio) and the CLI provider (ClaudeCodeCli) intentionally skip the circuit — they don't authenticate against an external billable API, so the IP-ban / over-billing risk doesn't apply.
  - **Convergence retained**: the SHA256-hashed key derivation + sliding-window semantics + open-duration timer are no longer brainarr-specific code — they live in (or layer on top of) the shared Common stack. The ecosystem-parity matrix row for AuthFailureGate is now ✓ across all four plugins.

See `ext/Lidarr.Plugin.Common/CHANGELOG.md` for the full catalog and [`docs/ECOSYSTEM_PARITY_MATRIX.md`](../ext/Lidarr.Plugin.Common/docs/ECOSYSTEM_PARITY_MATRIX.md) for the cross-plugin parity scorecard (30+ axes × 4 plugins).

## Z.AI Coding-Plan provider gotchas (`BrainarrZaiCodingProvider`)

Hard-won, live-confirmed (Lidarr E2E, real Coding-Plan key, May 2026). All three are easy to regress:

- **Never put `temperature` in the request body.** Z.AI's Anthropic-format Coding endpoint rejects *any* request carrying `temperature` with `[1210][Invalid API parameter]` (generic message, no field name — must be bisected). Claude Code, which the endpoint emulates, doesn't send it. Guarded by `CompleteAsync_OmitsTemperature`.
- **Keep `max_tokens` at the host default (2000); do NOT raise it for GLM's verbosity.** GLM wraps output in a ```json fence and pads. At 2000 a 50-item list truncates at the tail but the request *completes* (~10s); at 4096/8192 GLM fills the headroom and overruns the request timeout → `TimeoutException` → 0 items. Truncation is recovered downstream — `RecommendationJsonParser.SalvageObjectsFromText` extracts the array-element `{...}` objects (container-stack walk, string/escape-aware) and handles **both** shapes GLM emits: a bare root array `[…]` *and* an object-wrapped `{"recommendations":[…]}` whose outer brace never closes when truncated. That salvage path benefits every provider, not just GLM.
- **`thinking` does not help and is not sent.** The endpoint accepts Anthropic's `thinking:{type:disabled}` (no `[1210]`) but it has no effect — GLM-5.x slowness is raw generation speed (~47 tok/s), not a reasoning preamble. Don't add it back.
- **Reasoning models (GLM-5.x) need a longer timeout.** They run ~45–60s/request, so the default 30s AI timeout fails them (and a timeout returns nothing to salvage). The timeout error message points users to raise AI Request Timeout to 60–90s or pick the faster GLM-4.5-Air (~10s). The orchestrator's overall ~120s `SafeAsyncHelper` budget still caps GLM-5.x to ~2 top-up iterations regardless.
- **The first request after a Lidarr restart can time out** (TLS + raw-`HttpClient` first connection + model cold-start), including the 10s health-check probe. It self-heals on the next sync; don't mistake it for an auth/endpoint failure.
- The provider dispatches via a raw `System.Net.Http.HttpClient` (not Lidarr's `IHttpClient`) because `ManagedHttpDispatcher` throws on the `claude-cli` User-Agent the Coding-Plan filter requires.

### Claude Code Subscription (`BrainarrClaudeCodeSubscriptionProvider`) — OAuth Bearer, not x-api-key

The credential is a Claude Pro/Max **OAuth access token** (`claudeAiOauth.accessToken` from `claude login`), NOT an `sk-ant-` API key. Anthropic authenticates the OAuth family via **`Authorization: Bearer <token>` + `anthropic-beta: oauth-2025-04-20`** — sending it as `x-api-key` 401s (the original day-one bug, fixed 2026-05-30). Do NOT "simplify" back to x-api-key. The `anthropic-beta` value is dated and can rotate server-side. A `claude-cli` User-Agent is intentionally NOT set (Lidarr's `ManagedHttpDispatcher` throws on non-Lidarr UAs); if Bearer+beta prove insufficient for a real subscriber, migrate to a raw `HttpClient` like `BrainarrZaiCodingProvider` (queued). Auth header pinned by `CompleteAsync_UsesBearerOAuth_NotXApiKey`. Not live-testable without a subscription.

### Mainstream OpenAI-compatible providers (OpenAI/OpenRouter/DeepSeek/Groq/Perplexity)

Audited sound 2026-05-30: all use `Authorization: Bearer`, correct per-provider endpoints, `max_tokens` from the timeout-aware `request.MaxTokens` budget (never hardcoded), null-safe `choices[0].message.content` parsing, and `temperature` (correct for OpenAI format — opposite of ZaiCoding). They each **duplicate** `BuildRequestBody`/`SendAsync`/`ParseCompletion` (no shared base despite `BrainarrOpenAiCompatibleProvider` existing — queued dedup #46), so a contract change must touch all six. `CompleteAsync` auth+endpoint is now pinned per provider (`CompleteAsync_UsesBearerAuth_Against…Endpoint`) — keep that when refactoring. OpenRouter additionally requires `HTTP-Referer` + `X-Title` headers.

### OpenAI Codex Subscription (`BrainarrOpenAiCodexSubscriptionProvider`) — API-key-only today

Auth scheme is correct (`Authorization: Bearer` — right for OpenAI, both API keys and OAuth). **Known limitation (audited 2026-05-30): only the `OPENAI_API_KEY`-in-`auth.json` path works.** A pure ChatGPT-subscription OAuth token (`tokens.access_token` from `codex auth login`) 401s against `api.openai.com/v1/chat/completions` — the codex CLI uses the ChatGPT backend (Responses API + `chatgpt-account-id` header, neither spoken here; the loader doesn't even extract `account_id`). Deliberately NOT blind-fixed: unlike Claude's documented endpoint, the codex backend is undocumented/volatile (~50%+ a blind rewrite still fails, and it risks the working API-key path). The 401 hint routes users to add an `OPENAI_API_KEY`; real backend support is queued (#45) pending a subscriber to live-verify. Contract pinned by `CompleteAsync_UsesBearerAuth_AgainstChatCompletions`. **Lesson: same symptom as the Claude bug, different risk profile — a fix is only safe when the target endpoint/protocol is documented enough to pin with a test.**

### Z.AI GLM (`BrainarrZaiGlmProvider`) — the OPPOSITE-contract sibling

`BrainarrZaiGlmProvider` (PaaS, OpenAI-format `chat/completions`) and `BrainarrZaiCodingProvider` (Coding-Plan, Anthropic-format) are siblings that share `MapZaiHttpError` but have **deliberately opposite contracts** — do not unify them:
- **Temperature: ZaiGlm SENDS it, ZaiCoding OMITS it.** The OpenAI-format PaaS endpoint accepts `temperature`; the Anthropic-format Coding endpoint rejects it with `[1210]`. Guarded by `CompleteAsync_SendsTemperature` (ZaiGlm) vs `CompleteAsync_OmitsTemperature` (ZaiCoding). Removing temperature from ZaiGlm "by analogy" is a regression.
- **Transport: ZaiGlm uses Lidarr's `IHttpClient` (Bearer, any UA); ZaiCoding uses a raw HttpClient** (needs `claude-cli` UA the dispatcher forbids).
- Both use `request.MaxTokens ?? 2000` (timeout-aware budget via the adapter) and benefit from `RecommendationJsonParser` salvage on `CompleteAsync`.
- A Coding-Plan token used against the PaaS endpoint returns `1113` → mapped to `QuotaExceeded` with a hint to switch to the Coding provider.
- **Audited 2026-05-30: sound.** Two LOW/dormant asymmetries live only on `StreamAsync` (never called by the pipeline — adapter uses `CompleteAsync` only): streaming errors bypass `MapZaiHttpError` (1113→RateLimited), and outer-envelope truncation defeats salvage. Fix only if `StreamAsync` is ever wired in.

### Gemini (`BrainarrGeminiProvider`) — key is in the URL; MUST suppress host HTTP errors

Gemini's `generateContent` endpoint authenticates **only** via a `?key=AIza…` query param (no header auth) — so the API key is in the request URL. The host `IHttpClient` throws an `HttpException` on non-2xx whose message embeds the full URL, and NLog's exception renderer prints inner exceptions **unredacted** (`LogRedactor` only scrubs message strings, not exception objects; no target-level scrubber exists). There are TWO sites that put the key in the URL — `BrainarrGeminiProvider.SendAsync` AND `GeminiModelDiscovery` (the model-dropdown enumeration) — and BOTH must set `SuppressHttpError`. Therefore `SendAsync` **must** set `request.SuppressHttpError = true` so non-2xx responses are *returned*, not thrown, and get mapped through the `CompleteAsync`/`CheckHealthAsync` status-code branches with `inner: null`. **Do NOT remove that flag** — it was a HIGH key-leak fix (2026-05-30), pinned by `CompleteAsync_PinsEndpointKeyInQuery_AndSuppressesHttpError` + `CompleteAsync_AuthError_DoesNotChainUrlBearingException`. Contrast: Anthropic uses the same `hex`-as-inner mapping but is safe because its key is in the `x-api-key` header (the host exception renders the URL, not headers) — so Anthropic must keep its throw-based error path (no SuppressHttpError; its `CompleteAsync` has no returned-non-2xx branch). Audited 2026-05-30: both parse null-safe (Gemini survives SAFETY/MAX_TOKENS empty-parts), `maxOutputTokens`/`max_tokens` flow from the budget. LOW queued: Gemini sends the system prompt concatenated into the user part, not the native `systemInstruction` field.

## Recommendation engine: budgets, attainment, style-seeded discovery

Four engine behaviors that are easy to regress (added 2026-05-29):

- **Overall fetch budget respects the user's timeout.** `BrainarrSettings.GetOverallFetchTimeoutMs()` = `AIRequestTimeoutSeconds × (1 + top-up iterations) + FetchOverheadSeconds`, floored at the legacy 120s, capped at `MaxOverallFetchTimeoutMs` (30min), passed to `SafeAsyncHelper.RunSafeSync` in `BrainarrOrchestrator.FetchRecommendations`. It mirrors the local-provider (Ollama/LM Studio) per-request elevation to `LocalProviderDefaultTimeout` so the budget never starves a single local call. **Do not** revert `FetchRecommendations` to the parameterless `RunSafeSync` (hardcoded 120s) — that silently caps slow-model runs mid-top-up.
- **`max_tokens` is timeout-aware, not flat.** `GetOutputTokenBudget()` scales to `MaxRecommendations` but is bounded by `AIRequestTimeoutSeconds × ConservativeOutputTokensPerSecond` (overshooting cancels the HTTP call mid-stream → no body to salvage, strictly worse than a clean truncation), floored at `DefaultMaxTokens` (2000). Pushed via `TimeoutContext.Push(seconds, maxOutputTokens)` at both the initial (`RecommendationGenerator`) and top-up (`TopUpPlanner`, after the deficit `MaxRecommendations` scope) sites; read by `LlmProviderAdapter` via `GetMaxOutputTokensOrDefault(_maxTokens)`. This is the *output/completion* cap, not the context window. **Do not add a per-provider flat max-tokens setting** (a dead `LMStudioMaxTokens` was removed for this reason) — a fixed cap conflicts with the timeout-aware budget and can overshoot the request timeout, losing the response body. Temperature is different: it's centrally defaulted to 0.8 in the adapter, but **LM Studio** (the tunable local provider) honours `LMStudioTemperature` (default 0.5), wired via `ProviderRegistry` and clamped to `[0.0, 2.0]`; other providers keep 0.8.
- **Attainment vs provider success.** `RecordRunSummary` logs `attainment=items/target%` (via `AttainmentPercent`, truncated) separately from `providerSuccess` (provider-health). Don't conflate them — provider success can read 100% while attainment is 50%.
- **Confidence floor (`MinConfidence`) + provenance.** Visible advanced setting (default 0.7), applied by `SafetyGateService` (drop, or queue when `QueueBorderlineItems`), validated to `[0,1]`. Parsers fabricate a default score when the model omits one, so each `Recommendation` carries **`ConfidenceProvided`** (default true; parsers set false when fabricating). The gate is `!ConfidenceProvided || Confidence >= floor` — the floor only filters *explicitly* low-scored items; score-less ones are kept (no silent cliff above the fabricated default). **Critical gotcha:** the flag must survive every rebuild between parser and gate — `RecommendationSanitizer` (runs on the full set pre-gate) explicitly copies it, and the MBID resolvers (`MusicBrainzResolver`, `ArtistMbidResolver`) use record `with`-copies (a field-by-field `new Recommendation{…}` silently resets it to the default, making the fix a no-op — an adversarial review caught exactly this). When adding fields to `Recommendation`, prefer `with` over field-by-field rebuilds. The provenance now flows into the **review queue** too: `ReviewQueueService.ReviewItem` carries `ConfidenceProvided` (default true, so pre-field `review_queue.json` entries deserialize as provided — STJ honors the initializer for missing properties), copied in both `Enqueue` and the dequeue rebuild. `RecommendationTriageAdvisor` is now provenance-aware: when `!ConfidenceProvided` it **skips all confidence-derived scoring** (calibration, below-threshold penalties, the ≥0.9-with-MBID risk reducer), emits band `"unscored"` (a literal string, NOT a `ConfidenceBand` enum value — don't `Enum.Parse` the band downstream) and a Brainarr-local `CONFIDENCE_NOT_PROVIDED` reason, while still running the confidence-independent MBID/duplicate checks. Consequence: a score-less item can reach `review` (riskScore 5 from MBID+duplicate) but never `reject` (≥6, which needs the confidence penalties), and one failing only the MBID gate can auto-accept when *Auto-Apply Triage* is on (default off). `CONFIDENCE_NOT_PROVIDED` is Brainarr-local until promoted to Common's `TriageReasonCodes` (submodule — queued).
- **Two `RecommendationValidator` classes — the sync one is on the live path, and per-definition settings are resolved per run.** `Services.RecommendationValidator` (sync, `ValidateBatch`, ctor `(logger, customPatterns, strictMode)`) is the hallucination/pattern filter the pipeline actually runs; `Services.Validation.RecommendationValidator` (async `IRecommendationValidator`) is a separate, different-interface class. `RecommendationPipeline` consumes `Services.IRecommendationValidator` and validates via `ResolveValidator(customPatterns, strictMode)`: the DI-registered validator is a **process-wide singleton** built logger-only, but `CustomFilterPatterns`/`EnableStrictValidation` are **per-import-list-definition** settings — so `ResolveValidator` reuses the injected singleton when both are default (zero behavior change; a mock validator stays in effect under test) and builds a per-run `RecommendationValidator(_logger, patterns, strict)` only when the user configures either. Custom patterns are lowercased **plain substrings** (`albumLower.Contains`), NOT regex — no ReDoS surface; blank CSV entries are dropped. Strict mode only tightens albums that already match a `PossiblyLegitimatePattern` (e.g. `(deluxe)`, `(remastered)`) — an album whose parenthetical matches no possibly-legit pattern bypasses strict entirely (so `(2009 Remaster)` is NOT caught, because the pattern list has `(remastered)` not `remaster`). These two settings were dead until wave-3-reaudit (#67) wired them and un-hid them. NOTE: Lidarr `FieldDefinition(Order)` is a UI display-order hint, not a field identity — duplicate Order numbers (the file has several) are harmless; settings serialize by property name.
- **Discovery-mode escalation on dedup saturation.** In the top-up loop (`IterativeRecommendationStrategy`), when an aggressive/top-up run would early-stop on a low/zero unique-rate streak while still under target, it instead widens the effective `DiscoveryMode` one step (Similar→Adjacent→Exploratory, via `TryEscalateDiscoveryMode`), resets the streak, and continues — breaking out of the saturated cluster. Gated on the **caller's** `aggressiveGuarantee` (captured before the iteration-1 auto-enable), so the base single-pass strategy is unchanged. The escalated mode is applied to the prompt+sampling via a `SettingScope` around `BuildIterativePrompt` (synchronous window; settings.DiscoveryMode restored after). Escalation is bounded (2 steps max) and `maxIterations` growth — including the pre-existing aggressive bump — is hard-capped at `ESCALATION_ITERATION_CEILING` so a dribbling provider can't grow the budget without limit.
- **Per-run telemetry must NOT be a delta on a process-wide counter.** The confidence-floor starvation hint (run-summary "Under target" line naming `Minimum Confidence` when it gated items this run) counts floor drops via `GateMetricsContext` — an `AsyncLocal`-scoped mutable holder (same pattern as `TimeoutContext`), opened by `BrainarrOrchestrator.ExecuteWorkflowCoreAsync` around the coordinator call and written by `SafetyGateService`. The holder REFERENCE flows down to the gate (which runs inside the awaited pipeline) so its mutations are visible to the orchestrator after the await; concurrent fetches (a Test action overlapping a sync; multiple import-list definitions — `BrainarrSettings` has no `GetHashCode` override, and `PreventConcurrentFetch` serializes only by `fetch_{provider}_{settings-identity}`, so different instances run concurrently) each get an isolated holder. An earlier version used a cumulative `IPerformanceMetrics` counter read as a before/after delta — that RACED on the shared singleton metrics across concurrent different-key runs and over-stated the count (caught in adversarial review). Floor counting is provenance-aware: score-less items (`!ConfidenceProvided`) bypass the floor and are never counted; an MBID-gate drop is attributed to MBID, not confidence. The gate also emits a per-run reason-segregated Info log (`Safety gate: N passed, X below Minimum Confidence, Y missing MBID(s)`).
- **Long-lived in-memory collections must be bounded — the plugin process lives for weeks.** Any `static`/singleton-instance Dictionary/HashSet/List keyed by something high-cardinality (artist|album, recommendation key, cache key) is a slow heap leak unless capped/TTL'd. `DuplicationPreventionService._historicalRecommendations` (cross-session dedup in `DuplicationPrevention.cs`) is the cautionary tale: it was an unbounded `HashSet<string>` that grew forever (`ClearHistory()` has no production caller) — now a `Dictionary<string,DateTime>` capped at 50k, evicting oldest down to ~90% in ONE pass (compute the evict count from the actual overflow, not a fixed 10%, so the bound holds for any batch size). Confirmed-bounded: LimiterRegistry (`BoundedConcurrentDictionary`, capped+swept), MusicBrainzResolver `_recent` (512 LRU — its key is built from the **`HtmlDecode`d** artist/album, the same text the query and name-match use, so `"Simon &amp; Garfunkel"` and `"Simon & Garfunkel"` collapse to one key instead of firing a redundant query; deriving the cache key from the raw text while the work uses decoded text was a residual of the `&`-resolution fix), ArtistMbidResolver (`BoundedConcurrentDictionary`), circuit breakers / health / metrics dicts (keyed by low-cardinality provider name). `RecommendationHistory` dicts are now pruned (180-day retention via `CleanupOldEntries`, wired on load + a 6h throttle in `RecordSuggestions`; 180d is safely > every exclusion window — `RecentlyRejected` is 30d, `OverSuggested` is count-based but re-suggestion refreshes `LastSuggested` so an active artist is never pruned). `MetricsCollector._points` is now size-capped (10k per metric, trims to newest ~90% on overflow) in addition to the 24h TTL sweep. **Every long-lived in-memory collection is now verified bounded** (resolver caches, LimiterRegistry, dedup set, RecommendationHistory, metric points, ValidationMetrics, TokenCostEstimator). When adding a long-lived collection, give it a cap + eviction and a test that drives it past the cap.
- **Run cancellation must propagate through the WHOLE top-up/enrichment chain.** On the cancellation-aware path, a cancelled run token surfaces as `OperationCanceledException` and the orchestrator (`FetchRecommendationsAsync(settings, ct)`) maps it to an empty result. For that to work, **every** broad `catch (Exception)` between the provider call and that handler must let OCE through: `IterativeRecommendationStrategy` (per-iteration catch), `TopUpPlanner` (its sole caller — *also* had to be fixed; patching only the strategy was a no-op because TopUpPlanner re-swallowed it one frame up), and the two enrichment resolvers. The idiom is `catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }` — the `when` guard is load-bearing: a provider's OWN request timeout throws `OperationCanceled`/`TaskCanceled` while the run token is *not* cancelled, and that must stay a recoverable per-iteration failure (break + return partial), NOT abort the run. When adding a `catch (Exception)` anywhere on the fetch path, precede it with this guard. NOTE: the shipped `Fetch()` runs the **sync** path (`RunSafeSync` enforces the budget via wall-clock `Task.Wait` + a `None` token), so cancellation is inert in production today — this is correctness-hardening for the cancellation-aware/host-abort path. Heuristic: a propagated exception is only as good as the *farthest* catch it must clear — verify it reaches its intended handler, not just the first re-throw.
- **Style-seeded ("genre-first") discovery.** When the user selects/types styles their library has ZERO coverage of, recommend artists OF those styles rather than library-grounded ones. The gate is `sum(StyleContext.StyleCoverage[selectedSlug]) == 0`. **CRITICAL invariant:** the prompt (`LibraryPromptRenderer`) and the post-validation filter (`RecommendationPipeline.IsStyleSeededDiscovery`) MUST compute this from the *same* `StyleContext.StyleCoverage` signal — an earlier version used `LibraryProfile.TopGenres` + parent-relaxed `IsMatch` in the pipeline, which disagreed with the renderer for parent-style-over-child-genre selections and gutted results. Freestyle (non-catalog) styles pass through `DefaultStyleSelectionService` as `freestyle:`-slugged anchors with their own `MaxSelectedStyles` allowance.

## Development Status

**Current Status**: Production-ready v1.6.0 - Full implementation with comprehensive test suite

The project includes:

- Complete implementation with 11 AI providers (2 local, 7 cloud, 2 subscription)
- Comprehensive test suite (280+ test files across 50+ test categories)
- Production-ready architecture with advanced features
- Full documentation in `docs/` folder

## Architecture Overview

The implemented architecture includes:

### Multi-Provider AI System

- **Local-First Options**: Privacy-focused local providers (Ollama, LM Studio)
- **Cloud Integration**: 11 total providers including OpenAI, Anthropic, Google Gemini, 2 subscription providers, etc.
- **Provider Failover**: Automatic failover with health monitoring
- **Dynamic Detection**: Auto-detects available models for local providers

### Implemented Architecture

```text
Brainarr.Plugin/
├── Configuration/          # Provider settings and validation
│   ├── Constants.cs
│   ├── ProviderConfiguration.cs
│   └── Providers/          # Per-provider configuration classes
├── Services/
│   ├── Core/              # Core orchestration services
│   │   ├── AIProviderFactory.cs
│   │   ├── AIService.cs
│   │   ├── LibraryAnalyzer.cs
│   │   └── ProviderRegistry.cs
│   ├── Providers/         # AI provider implementations (11 providers)
│   ├── Support/           # Supporting services
│   ├── LocalAIProvider.cs
│   ├── ModelDetectionService.cs
│   ├── ProviderHealth.cs
│   ├── RateLimiter.cs
│   ├── RecommendationCache.cs
│   └── RetryPolicy.cs
├── BrainarrImportList.cs  # Main Lidarr integration
└── BrainarrSettings.cs    # Configuration UI

Brainarr.Tests/            # Comprehensive test suite
├── Configuration/         # Configuration tests
├── Services/Core/         # Core service tests
├── Services/              # Provider tests
├── Integration/           # End-to-end tests
└── EdgeCases/            # Edge case handling
```

### Key Technical Patterns

- **Provider Pattern**: Each AI service implements `IAIProvider` interface
- **Factory Pattern**: `AIProviderFactory` manages provider instantiation
- **Registry Pattern**: `ProviderRegistry` for extensible provider management
- **Health Monitoring**: Real-time provider availability tracking
- **Rate Limiting**: Per-provider rate limiting with configurable limits
- **Caching**: Intelligent recommendation caching to reduce API calls
- **Retry Policies**: Exponential backoff retry with circuit breaker patterns
- **Recommendation Modes**: Supports both artist-only and album-specific recommendations
- **Cross-Platform**: Windows, macOS, and Linux compatibility with platform-specific optimizations

## Implemented Features

### Core Functionality

- ✅ 11 AI providers (2 local + 7 cloud + 2 subscription)
- ✅ Auto-detection of local models
- ✅ Provider health monitoring
- ✅ Rate limiting and caching
- ✅ Comprehensive configuration validation with timeout bounds (5-600s)
- ✅ Library analysis and profiling
- ✅ Recommendation sanitization
- ✅ **Artist-only recommendation mode** - Import all albums by recommended artists
- ✅ **Dual recommendation modes** - Artists vs. specific albums
- ✅ **Music styles catalog** - Normalization, matching, and filtering
- ✅ **Circuit breaker pattern** - Prevents cascading failures with configurable thresholds

### Technology Stack

- **Platform**: .NET 8 (`net8.0` target framework — see Runtime & Docker Image Requirements above)
- **HTTP Client**: Lidarr's IHttpClient for provider communication
- **Configuration**: Lidarr's field definition system with validation
- **Logging**: NLog integration with structured logging
- **Testing**: Comprehensive test suite covering all components

## Development Workflow

For ongoing development:

1. **Build**: `dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -m:1`
2. **Test**: `dotnet test --blame-hang-timeout 30s`
3. **Package**: `./build.ps1 -Package`
4. **Debug**: Enable debug logging in Lidarr settings

### Common Development Commands

```bash
# Build plugin (single-threaded to avoid Windows file-lock issues)
dotnet build Brainarr.Plugin/Brainarr.Plugin.csproj -m:1

# Run full test suite (blame-hang-timeout prevents Spectre.Console hangs)
dotnet test --blame-hang-timeout 30s

# Run specific test categories
dotnet test --filter Category=Integration --blame-hang-timeout 30s
dotnet test --filter Category=EdgeCase --blame-hang-timeout 30s

# Package for deployment
./build.ps1 -Package
```

## CI/CD Pipeline Solution

**RESOLVED**: The GitHub Actions CI was previously failing due to missing Lidarr assembly dependencies. This has been definitively solved using the plugins Docker image extraction approach.

### The Working Solution

The CI workflow now uses pre-built Lidarr assemblies instead of trying to build from source:

```yaml
- name: Download Lidarr Assemblies
  run: |
    echo "Downloading Lidarr assemblies from latest release..."
    mkdir -p ext/Lidarr/_output/net8.0

    # Get latest release URL dynamically
    LIDARR_URL=$(curl -s https://api.github.com/repos/Lidarr/Lidarr/releases/latest | grep "browser_download_url.*linux-core-x64.tar.gz" | cut -d '"' -f 4 | head -1)

    if [ -n "$LIDARR_URL" ]; then
      curl -L "$LIDARR_URL" -o lidarr.tar.gz
    else
      # Fallback to known version
      curl -L "https://github.com/Lidarr/Lidarr/releases/download/v2.13.1.4681/Lidarr.main.2.13.1.4681.linux-core-x64.tar.gz" -o lidarr.tar.gz
    fi

    tar -xzf lidarr.tar.gz

    # Copy required assemblies
    cp Lidarr/Lidarr.Core.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Common.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Http.dll ext/Lidarr/_output/net8.0/
    cp Lidarr/Lidarr.Api.V1.dll ext/Lidarr/_output/net8.0/
```

### Why This Works

1. **Stable Dependencies**: Uses official Lidarr release binaries that are tested and stable
2. **No Compilation**: Eliminates complex build dependencies and potential source compilation failures
3. **Fast & Reliable**: Simple download and extract, much faster than building from source
4. **Cross-Platform**: Works identically across Ubuntu, Windows, and macOS runners
5. **Maintainable**: Clear, understandable workflow that's easy to debug

### Key Learnings

- **Never build Lidarr from source in CI** - it's complex, slow, and error-prone
- **Use pre-built assemblies** - download from GitHub releases instead
- **Dynamic URL detection** - use GitHub API to get latest release URLs with fallbacks
- **Proper error handling** - validate downloads and extractions with clear error messages
- **Cross-platform testing** - test on multiple OS/runtime combinations

### Local Development Setup

The project's `.csproj` file has sophisticated Lidarr path resolution that automatically finds assemblies in:
1. Command line: `-p:LidarrPath=...`
2. Environment: `LIDARR_PATH`
3. Local submodule: `ext/Lidarr/_output/net8.0`
4. System installations: `/opt/Lidarr`, `C:\ProgramData\Lidarr\bin`, etc.

For local development, ensure Lidarr assemblies are present in `ext/Lidarr/_output/net8.0/` or set the `LIDARR_PATH` environment variable.

### CI Status: ✅ WORKING

The CI pipeline now successfully:
- ✅ Downloads Lidarr assemblies from GitHub releases
- ✅ Builds plugin across 3 environments (Ubuntu/Windows/macOS × .NET 8.0.x)
- ✅ Runs comprehensive test suite (280+ test files)
- ✅ Performs security analysis with CodeQL
- ✅ Creates release packages on tagged releases

**This solution has been UPGRADED with TypNull's proven Docker approach and should not require further changes.**

### Latest Improvements (Based on TypNull's Tubifarry Plugin)

The CI has been enhanced with the proven Docker extraction method:

**Key Improvements:**
- ✅ **Docker Assembly Extraction**: Uses `ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` (plugins branch)
- ✅ **Minimal NuGet.config**: Eliminates private feed authentication issues
- ✅ **CI-Optimized Project File**: Fallback approach with essential dependencies only
- ✅ **Consistent Across All Jobs**: Same Docker approach for build, test, security scan, and release
- ✅ **Environment Variables**: Centralized configuration for maintainability

**Why This Works Better:**
1. **Plugins Branch Compatibility**: Uses actual plugins branch assemblies instead of main branch
2. **No Private NuGet Issues**: Avoids Servarr Azure DevOps feed authentication problems
3. **Proven Success**: Based on first successful Lidarr plugin CI (TypNull's Tubifarry)
4. **More Reliable**: Docker extraction is more stable than building from source

### Update (September 2025)

- Use the Lidarr plugins Docker image (`ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}`) to extract assemblies in CI and local scripts.
- The older release-tarball instructions below are retained for historical context only and should not be used for main CI paths.

## Docker E2E Harness (wave 22b)

A runnable end-to-end harness boots a real Lidarr container, mounts the merged
Brainarr plugin DLL, waits for the API, and asserts plugin liveness against
Lidarr's REST API. This is the smoke alarm for "did the plugin actually load
inside the host?" — sandbox tests cannot answer that.

Brainarr is an **ImportList-only plugin** (`HasIndexer=false`,
`HasDownloadClient=false` in `BrainarrModule`), so the matrix is 2 facts (not
4 like indexer/downloadclient plugins):

| Test | Asserts |
|------|---------|
| `Plugin_Loads_AppearsInImportListSchema` | `GET /api/v1/importlist/schema` lists Brainarr |
| `ImportList_Test_WithEmptySettings_ReturnsSensibleFailure` | `POST /api/v1/importlist/test` returns non-5xx (validation failure, not plugin-load failure) |

### Run locally

```powershell
pwsh scripts/e2e.ps1                                    # build + run
pwsh scripts/e2e.ps1 -SkipBuild                         # re-run without rebuild
pwsh scripts/e2e.ps1 -Filter 'FullyQualifiedName~ImportList_Test'
```

If Docker Desktop isn't running, the tests **skip gracefully** rather than
fail — they're safe to leave in any local test command.

### Pinned image

`ghcr.io/hotio/lidarr:pr-plugins-3.1.2.4913` (single-plugin instance on host
port `8693` to avoid clashes with tidalarr `8690`, applemusicarr `8691`,
qobuzarr `8692`). Plugin mount: `/config/plugins/RicherTunes/Brainarr`.

### How it's wired

- `Brainarr.Tests/Runtime/BrainarrLidarrContainerFixture.cs` — subclass of
  common's `LidarrContainerFixture` with brainarr-specific
  `LidarrContainerOptions` (image, port, mount path, DLL discovery,
  `"Brainarr"` schema-entry substring).
- `Brainarr.Tests/Runtime/DockerE2ETests.cs` — 2 `[SkippableFact]`s that
  delegate to `AssertPluginAppearsInImportListSchemaAsync` and
  `AssertImportListTestReturnsSensibleFailureAsync` (both added to common
  in wave 22b alongside the existing indexer/downloadclient assertions).

## Local Verification (Billing-Blocked CI)

When GitHub Actions billing is blocked, run the merge-critical verification pipeline locally:

```bash
pwsh scripts/verify-local.ps1                    # Full pipeline (extract + build + package + closure + E2E)
pwsh scripts/verify-local.ps1 -SkipExtract       # Fast rerun (reuse cached host assemblies)
pwsh scripts/verify-local.ps1 -SkipTests         # Build + packaging closure only
pwsh scripts/verify-local.ps1 -NoRestore         # Skip dotnet restore (fast iteration)
pwsh scripts/verify-local.ps1 -IncludeSmoke      # + Docker smoke test (mounts plugin in Lidarr)
```

**Prerequisites**: PowerShell 7+ (`pwsh`), .NET 8 SDK, Docker (for extract/smoke stages).

The script delegates to `ext/Lidarr.Plugin.Common/scripts/local-ci.ps1`, which orchestrates the same gates as CI: host assembly extraction with .NET 8 + FV 9.5.4 guardrails, plugin packaging via `New-PluginPackage`, and packaging closure validation via `generate-expected-contents.ps1 -Check`.

## Local Development Setup

1. **Prerequisites**:
   - .NET 8 SDK (the plugin targets `net8.0`)
   - Lidarr development environment
   - At least one AI provider (Ollama recommended for testing)

2. **Development Environment**:
   - IDE: Visual Studio, VS Code, or JetBrains Rider
   - Testing: Local Lidarr instance for plugin testing
   - AI Providers: Local Ollama installation recommended

## Plugin Module Capability Flags

`BrainarrModule` (in `Brainarr.Plugin/Hosting/BrainarrModule.cs`) extends `StreamingPluginModule` from Common and correctly overrides capability flags:

- `HasIndexer() => false` -- Brainarr is an import list only, not an indexer
- `HasDownloadClient() => false` -- Brainarr is an import list only, not a download client

These flags ensure the Lidarr plugin host does not attempt to register Brainarr as an indexer or download client provider.

## Bridge Wiring

Brainarr does **not** wire up the active host-bridge contracts from Common (e.g., `IStreamingBridge`, download-client bridge, indexer bridge). This is intentional: LLM-based import list plugins follow a different integration pattern than streaming/download plugins. Brainarr communicates with AI providers via HTTP and returns `ImportListItemInfo` results directly -- it does not need the bridge abstractions designed for streaming services like Tidal or Qobuz.

However, `BrainarrModule.ConfigureServices` **does** call `services.AddBridgeDefaults()` (see `Brainarr.Plugin/Hosting/BrainarrModule.cs`). This registers Common's no-op default handlers (`DefaultAuthFailureHandler`, `DefaultIndexerStatusReporter`, `DefaultDownloadStatusReporter`, `DefaultRateLimitReporter`) so that any common-library helper that optionally resolves a bridge handler gets a safe fallback rather than throwing `NRE`. It also satisfies the cross-plugin ecosystem parity contract (`Check_RegistersBridgeDefaults` in `EcosystemParityTestBase`).

## Security Considerations

- API keys stored securely through Lidarr's configuration system
- Local providers prioritized to avoid data transmission
- No sensitive music library data logged or transmitted unnecessarily
- Rate limiting and error handling for cloud providers

## Specialist Development Guidance

Based on the task context, apply the appropriate specialist expertise:

### 🤖 AI Provider System Specialist

**When working with**: provider implementations, failover logic, model detection, health monitoring

**Key Focus Areas**:
- **IAIProvider Interface**: Follow established contract in `Services/Providers/`
- **Provider Registry**: Use `ProviderRegistry.cs` patterns for extensible provider management
- **Health Monitoring**: Implement `ProviderHealth.cs` patterns for availability tracking
- **Model Detection**: Follow `ModelDetectionService.cs` for local provider auto-detection
- **Authentication**: Provider-specific auth patterns in `Configuration/Providers/`
- **Rate Limiting**: Per-provider limits using `RateLimiter.cs` patterns
- **Error Handling**: Implement proper retry policies with circuit breaker patterns

**Implementation Patterns**:

```csharp
// Provider implementation template
public class NewProvider : IAIProvider
{
    public async Task<bool> TestConnectionAsync() { /* Health check */ }
    public async Task<List<string>> GetAvailableModelsAsync() { /* Model detection */ }
    public async Task<List<ImportListItemInfo>> GetRecommendationsAsync() { /* Core logic */ }
}
```

### ⚙️ Configuration System Specialist

**When working with**: settings, validation, UI integration, provider configuration

**Key Focus Areas**:
- **Dynamic UI**: Use Lidarr's field definition system with conditional visibility
- **Validation**: FluentValidation patterns in `ProviderConfiguration.cs`
- **Settings Classes**: Provider-specific configuration in `Configuration/Providers/`
- **Backwards Compatibility**: Migration patterns for configuration changes
- **UI Integration**: Follow `BrainarrSettings.cs` patterns for seamless UX

**Implementation Patterns**:

```csharp
// Configuration validation template
When(c => c.Provider == AIProvider.NewProvider, () =>
{
    RuleFor(c => c.NewProviderApiKey)
        .NotEmpty()
        .WithMessage("API key is required for NewProvider");
});
```

### 🧪 Testing & Quality Specialist

**When working with**: tests, mocking, coverage, quality assurance

**Key Focus Areas**:
- **Test Structure**: Follow `Brainarr.Tests/` organization patterns
- **Test Categories**: Use `[Trait("Category", "...")]` for Integration, EdgeCase, Unit
- **Mocking Strategy**: Consistent mocking with Moq framework
- **Edge Cases**: Comprehensive error condition testing
- **Integration Tests**: End-to-end workflow validation
- **Performance Tests**: Provider response time and resource usage

**Implementation Patterns**:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task Provider_Should_HandleFailover_WhenPrimaryUnavailable()
{
    // Arrange: Mock primary provider failure
    // Act: Trigger failover scenario
    // Assert: Verify secondary provider usage
}
```

### 🔧 CI/CD & Build Specialist

**When working with**: GitHub Actions, builds, deployment, dependencies

**Key Focus Areas**:
- **Assembly Management**: NEVER build Lidarr from source - use pre-built assemblies
- **Cross-Platform**: Test on Ubuntu/Windows/macOS with .NET 8.0.x
- **Dependency Resolution**: Follow `.csproj` Lidarr path resolution patterns
- **Security Scanning**: CodeQL integration and vulnerability assessment
- **Release Automation**: Tagged release packaging and asset distribution

**Critical Rules**:
- ❌ Never: `git submodule update --recursive` (complex, error-prone)
- ✅ Always: Download pre-built Lidarr assemblies from GitHub releases
- ✅ Always: Use matrix strategy for comprehensive platform testing
- ✅ Always: Validate assembly downloads with proper error handling

### ⚡ Performance & Architecture Specialist

**When working with**: optimization, caching, memory management, scalability

**Key Focus Areas**:
- **Caching Strategy**: Intelligent caching with `RecommendationCache.cs`
- **Rate Limiting**: Provider-specific limits with `RateLimiter.cs`
- **Memory Management**: Efficient object lifecycle in provider implementations
- **Async Patterns**: Proper async/await usage for I/O operations
- **Circuit Breaker**: Resilient failure handling with `RetryPolicy.cs`
- **Resource Optimization**: HTTP client reuse and connection pooling

**Performance Patterns**:

```csharp
// Efficient caching pattern
var cacheKey = $"{provider}:{libraryProfile}:{timestamp}";
if (cache.TryGetValue(cacheKey, out var cached))
    return cached;
```

### 📚 Documentation & User Experience Specialist

**When working with**: documentation, user guides, API docs, code comments

**Key Focus Areas**:
- **Technical Accuracy**: Keep documentation synchronized with implementation
- **User Configuration**: Clear setup guides for each provider type
- **API Documentation**: Comprehensive provider interface documentation
- **Code Comments**: Meaningful inline documentation for complex logic
- **Architecture Docs**: Maintain `docs/` folder technical documentation

**Documentation Standards**:
- Use consistent terminology across all documentation
- Include practical examples for configuration patterns
- Maintain backwards compatibility notes for breaking changes
- Document security considerations for each provider type

## Context-Sensitive Activation

Claude Code will automatically apply the appropriate specialist context based on:

- **Provider/AI/Failover mentions** → AI Provider System Specialist
- **Config/Settings/Validation mentions** → Configuration System Specialist
- **Test/Mock/Coverage mentions** → Testing & Quality Specialist
- **CI/Build/Deploy mentions** → CI/CD & Build Specialist
- **Performance/Cache/Memory mentions** → Performance & Architecture Specialist
- **Docs/README/Comments mentions** → Documentation & UX Specialist
- Always use gh or git commands to validate the status of a build or any other validations.
- NEVER MERGE FIRST, TEST SECOND - Always test changes in their branch before merging to main. Even "safe-looking"
  dependency updates can introduce breaking changes, version conflicts, or runtime incompatibilities.

## Flaky Tests Policy

**Flaky tests are priority tech debt that must be paid immediately.** A test that passes sometimes and fails sometimes erodes trust in the entire test suite. When a flaky test is discovered:

1. **Fix it before starting new feature work** — flaky tests block reliable CI
2. **Document the root cause** in a commit message so the pattern is not repeated
3. **Never skip or disable** a flaky test without a tracking issue

### Known Flaky Tests (Brainarr)

| Test | Root Cause | Fix |
|------|-----------|-----|
| `E2EHermeticGateTests.LogRedaction_*` | **Fixed.** NLog config race — tests use `TestLogger.Create()` which mutates global `LogManager.Configuration` but class lacked `[Collection("LoggingTests")]`, allowing parallel execution with other NLog tests. | Added `[Collection("LoggingTests")]` to `E2EHermeticGateTests`. |
| `LoggerWarnOnceTests.WarnOnceWithEvent_Logs_OnlyOnce_PerKey` | **Fixed.** Static `_warnOnceKeys` dictionary persists across tests — if another test used the same event+key combo, this test sees 0 logs. | Added `LoggerExtensions.ClearWarnOnceKeysForTests()` call in constructor. |
| `BrainarrOrchestratorTopUpTests.FetchRecommendations_WithTopUpEnabled_FillsToTarget` | **Fixed.** Two issues: (1) `MusicBrainzResolver.EnrichWithMbidsAsync` catch block silently dropped recommendations on HTTP failure instead of preserving them (production bug). (2) Test created real resolvers that hit the MusicBrainz API, making results non-deterministic. | Production fix: added `result.Add(rec)` in catch block. Test fix: injected pass-through mock resolvers to eliminate external HTTP calls. |
| `LibraryAnalyzerTests.AnalyzeLibrary_DeterminesDiscoveryTrend` | **Fixed.** `CreateArtist` helper used `new Random().Next(1, 1000)` for IDs — birthday-problem collisions (~1%) caused `ToDictionary` to throw `ArgumentException`, caught by `AnalyzeLibrary`'s catch-all which returns fallback "stable collection" instead of "rapidly expanding". | Replaced `new Random().Next()` with `Interlocked.Increment(ref _nextArtistId)` for deterministic unique IDs. |
| `RateLimiter_WithThreadPoolExhaustion_StillEnforcesLimits` | **Fixed (v3).** Thread pool starvation under full-suite load (2400+ tests) caused `TaskCanceledException` even at 10@5/sec with 30s timeout. | Reduced to 5 requests at 3/sec with 60s timeout (PR #513). |
| `Performance_enhanced_cache_within_10pct_of_basic` | **Fixed (v2).** 50ms absolute floor still too aggressive — enhanced cache hit 71.2ms under full-suite load. | Increased floor from 50ms to 200ms (PR #513). Dual threshold: 10x relative OR 200ms floor. |
| `LimiterRegistryBoundedTests.Insert_AtCapacity_BoundsAllDicts` (+ sibling `LimiterRegistryMaintenanceTests`) | **Fixed (2026-05-30).** The three classes shared `[Collection("LimiterRegistryBounded")]` but **no `[CollectionDefinition]` existed for that name**, so the collection ran in parallel with everything. `LimiterRegistry`'s `_throttleUntil`/`_overrides` are process-wide statics; other parallel collections mutate them via `ConfigureFromSettings`/`RegisterThrottle`/`ResetForTesting`, racing the test's exact-state assertion (a concurrent insert at cap clear-all-evicts the just-added entry). Passed in isolation, flaked ~1/3 full runs. | Added `LimiterRegistryBoundedCollection.cs` with `[CollectionDefinition("LimiterRegistryBounded", DisableParallelization = true)]` — same mechanism `OrchestratorIntegration` uses — serializing all LimiterRegistry-static-state tests. With the race gone, restored the strong clear-then-insert assertion and added a race-immune bound check via internal `ThrottleEntryCountForTesting`/`DictCapForTesting`. Verified green across 8 consecutive full-suite runs. |

### Quarantined Tests (OOM — crash test host)

These stress tests allocate large datasets that exhaust test host memory. They are excluded from default runs via `[Trait("State", "Quarantined")]` but remain discoverable via `--filter "State=Quarantined"`.

| Test | File | Status |
|------|------|--------|
| `Cache_Should_HandleMillionOperations` | SecurityTestSuite.cs | Quarantined (1M iterations, 10K via CI guard) |
| `Cache_WithVeryLargeData_HandlesMemoryPressure` | CacheAndConcurrencyTests.cs | Quarantined (100×1000 items, 10×100 via CI guard) |
| `StressTest_MemoryPressure_HandlesGracefully` | EnhancedConcurrencyTests.cs | Quarantined (100 tasks × 1000 items, no CI guard) |

**Note (2026-05-24)**: `Cache_UnderMemoryPressure_EvictsOldEntries` (`ResourceAndTimeTests.cs`) and `StressTest_ManyRecommendations` (`EndToEndTests.cs`) — previously listed here — are no longer present in the codebase (removed or never written). The 3 above are the current quarantined set.

**Revival candidates**: The first two CI guards likely make those tests safe to un-quarantine. A future wave should verify by running them on a constrained CI image and dropping the `[Trait("State", "Quarantined")]` if they don't OOM. The third needs a CI guard added (or smaller default sizes) before un-quarantining.
