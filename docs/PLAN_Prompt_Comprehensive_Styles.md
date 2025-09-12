# Prompt Strategy Upgrade — Comprehensive Sampling + Dynamic Music Styles

Date: 2025-09-11
Owner: Brainarr team
Status: Proposed (ready to implement)

## Summary
Improve the prompt-building strategy for the “Comprehensive” sampling path to safely leverage larger context windows and add a user‑selectable Music Styles filter sourced from a dynamic JSON catalog. When styles are chosen, recommendations must strictly match those styles. When no styles are selected, prompts remain library‑centric and avoid generic content.

User decisions (confirmed):
- Styles catalog auto‑updates from our GitHub address; the JSON is always reachable.
- Cap the number of selected styles; documentation should explain pros/cons of many selections.
- If the user selects a style, enforce a strict style match (no widening beyond selected styles). We may expose a hidden toggle to relax in the future, default OFF (strict).

---

## Goals
- Enable users to filter recommendations by one or more Music Styles selected from a dynamic, maintained JSON catalog.
- Ensure prompts focus on the user’s Lidarr library context (artists/albums/genres) instead of generic guidance, especially when no styles are selected.
- Exploit larger token budgets in “Comprehensive” mode while staying under model limits with graceful trimming.

## Proposed Changes

### 1) Styles Catalog (data + service)
- Data file: `Brainarr.Plugin/Resources/music_styles.json`
  - Shape per entry: `{ "name": "Progressive Rock", "aliases": ["Prog Rock","Prog"], "slug": "progressive-rock", "parents": ["Rock"] }`.
  - ~2–5k entries. Aliases + parent relationships to support fuzzy matching and grouping.
- Service: `StyleCatalogService`
  - Responsibilities:
    - Load JSON at startup and cache in memory; keep a last‑good snapshot.
    - Auto‑update: periodically fetch from GitHub raw URL (config in constants) with ETag/If‑None‑Match support; default every 24h.
    - Fallback to embedded resource if remote unavailable; log once per outage window.
    - APIs:
      - `IReadOnlyList<Style> GetAll()`
      - `IEnumerable<Style>` `Search(string query, int limit = 50)` (typeahead)
      - `ISet<string>` `Normalize(IEnumerable<string> selected)` (alias → canonical slug)
      - `bool IsMatch(ICollection<string> genres, ISet<string> selectedSlugs)` (alias/parent aware)
- Constants (new):
  - `BrainarrConstants.StylesCatalogUrl` (GitHub raw), `StylesCatalogRefreshHours = 24`, `StylesCatalogTimeoutMs`.

### 2) UI: TagSelect for Music Styles
- Settings property in `BrainarrSettings`:
  - `IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();`
  - `[FieldDefinition(Label="Music Styles", Type=FieldType.TagSelect, SelectOptionsProviderAction="styles/getoptions", HelpText="Select styles (aliases supported). Leave empty to use your library profile.")]`
- Options provider endpoint in `BrainarrOrchestrator`:
  - Route: `styles/getoptions` → `{ options: [{ value: "progressive-rock", name: "Progressive Rock" }, ...] }`.
  - Backed by `StyleCatalogService.Search(query)`; cap results to 50.
- Selection cap:
  - Soft cap default: 10 styles. If exceeded, apply only the first 10 by library prevalence and log a warning.
  - New (hidden) advanced setting: `MaxSelectedStyles` (default 10, hidden).

### 3) Library Sampling: Strict Style Filtering
- When `StyleFilters` non‑empty:
  - Normalize selections via catalog; prefilter `allArtists`/`allAlbums` to items whose `Genres` intersect selected styles (alias/parent aware).
  - Strict mode (default): do not widen beyond selected styles. If result set is too small, proceed but log “low coverage” warning.
  - Hidden switch (future): `RelaxStyleMatching` (default false, hidden). When enabled, allow parent/adjacent widening.
- When `StyleFilters` empty: keep current behavior, but prompts must still emphasize existing library genres and duplication avoidance.

### 4) Prompt Assembly Changes
- If styles selected, add a dedicated block near the top:
  - `STYLE FILTERS: Progressive Rock, Art Rock (aliases recognized: Prog Rock, Prog)`
  - Recommendation rule: “Return items that belong to these styles only. Do not recommend outside these styles.”
- Keep existing “Comprehensive” preamble and library sample sections; compress lists further (see Token Budget).

### 5) Token Budget — Higher, Safe Ceilings for “Comprehensive”
- Extend `GetTokenLimitForStrategy` with a model‑aware table:
  - OpenAI o4‑mini/4o‑mini: 64k (conservative)
  - Anthropic Claude 3.7 family: 120k
  - Groq Llama‑3.1‑70B: 32k
  - DeepSeek/Perplexity/Gemini/Qwen: 32k default unless model hints known
  - Unknown → current 20k default
- New advanced override (hidden): `ComprehensiveTokenBudgetOverride` (int?)
- List compression to spend tokens on the most relevant bits:
  - Group albums by artist: `Artist — [Album1; Album2; …]`
  - Chunk lines to ~5–7 items; elide with “+ N more …” when needed.
- Re‑estimate after assembly; if overshoot → trim albums, then artists, then optional metadata sections.

### 6) Iterative Strategy with Style Filters
- With `SamplingStrategy == Comprehensive` and styles selected:
  - Allow +1–2 `MaxIterations` (respect upper bounds) because strict filters narrow candidate space.
  - Keep duplicate and library de‑dup unchanged.

### 7) Telemetry & Guardrails
- Log per run:
  - `SampledArtists`, `SampledAlbums`, `EstimatedTokens`, `AppliedStyleCount`, `Budget`, `Trimmed=true/false`.
- Warnings:
  - Too many selected styles (beyond cap) and low library coverage.
- Sanitize any user‑provided terms before inserting into prompts.

### 8) Tests
- Unit tests:
  - Style normalization (aliases, parents, case) and strict matching behavior.
  - Options provider returns expected items; handles empty query.
  - Prompt includes `STYLE FILTERS` block when styles selected; absent otherwise.
  - Token budget mapper returns larger limits for known models; override respected.
  - Sampling respects strict style filter; warns on low coverage.
- Integration tests:
  - Medium/large synthetic library with styles → token estimate under cap; rules mention styles; recommendations are filtered by validator.

### 9) Documentation
- Settings help: explain style selection, caps, and impact of selecting many styles (more focused vs. possible sparsity and larger prompts).
- Note that “Comprehensive” mode now uses model‑aware token budgets and includes stronger list compression.

---

## Implementation Details
- Files/Types:
  - `Brainarr.Plugin/Resources/music_styles.json` (embedded default); remote at `BrainarrConstants.StylesCatalogUrl`.
  - `Brainarr.Plugin/Services/Styles/StyleCatalogService.cs` (+ `Style` DTO).
  - `Brainarr.Plugin/BrainarrSettings.cs`: add `StyleFilters`, `MaxSelectedStyles` (hidden), `ComprehensiveTokenBudgetOverride` (hidden), `RelaxStyleMatching` (hidden, default false).
  - `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs`: `styles/getoptions` endpoint.
  - `Brainarr.Plugin/Services/LibraryAwarePromptBuilder.cs`: style‑aware filtering in sampling; style block + strict rule in prompt.
  - `Brainarr.Plugin/Configuration/Constants.cs`: add URL/timeouts for catalog; token budget model table.
  - Tests in `Brainarr.Tests/Services/…` for styles service, prompt builder, options provider, and token budget mapping.

- Auto‑update flow:
  1) On startup: load embedded JSON → async fetch remote with ETag → if 200/304, update cache.
  2) Background refresh every `StylesCatalogRefreshHours` (default 24h).
  3) UI “Test”/“Reload” action (optional later) to force refresh.

- Selection cap logic:
  - If `StyleFilters.Count > MaxSelectedStyles`, keep the subset with highest library coverage first, then alphabetical; log Info with counts.

---

## Risks & Mitigations
- Large catalog could overwhelm the UI → typeahead only, limit 50 results, debounce on the client.
- Genre<→style mismatches may reduce matches → aliases + parents, but strict mode keeps guarantees; warn on low coverage.
- Cost/latency from larger prompts → conservative model limits, strong trimming, override available.
- Very wide selections dilute focus → selection cap + doc explaining tradeoffs.

## Definition of Done
- Music Styles TagSelect appears and persists; options load dynamically from catalog.
- With styles selected, prompt includes style block and strict rule; sampling filters to styles.
- Without styles, prompt remains library‑centric and avoids generic content.
- Comprehensive path respects larger budgets safely with compression and trimming.
- Unit tests green; short docs/help text updated.

## Rollout (2 PRs)
- PR1: Catalog + service + settings field + options endpoint + unit tests.
- PR2: Style‑aware sampling + prompt changes + token budget table + compression + tests + docs.

## Appendix A — TagSelect Options Contract
- Route: `styles/getoptions`
- Request: standard TagSelect query (supports `query` parameter for typeahead)
- Response: `{ options: [{ value: string (slug), name: string (label) }] }`

---

## Appendix B — Hidden Settings (for maintainers)
- `MaxSelectedStyles` (Number; default 10) — cap applied styles; hidden.
- `ComprehensiveTokenBudgetOverride` (Number) — override model‑aware budget; hidden.
- `RelaxStyleMatching` (Checkbox; default false) — when true, allow parent/adjacent widening; hidden (strict is default per user decision).
