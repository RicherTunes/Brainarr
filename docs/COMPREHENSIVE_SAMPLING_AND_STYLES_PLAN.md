# Comprehensive Sampling + Dynamic Music Styles — Scalable Plan and UX Improvements

## Executive Summary

This plan implements a scalable, user-friendly “Comprehensive” sampling flow with strict, user‑selectable Music Style filters backed by a dynamic JSON catalog. It challenges and refines the original proposal based on the current codebase:

- Centralize token budgets using provider/model awareness and existing capability services instead of hardcoded constants in the prompt builder.
- Add a Style Catalog + Library Style Index that supports strict filtering and fast lookups, with graceful fallbacks and robust telemetry.
- Improve UX with a TagSelect that prefers library‑relevant styles by default, shows coverage counts, and supports quick presets (bundles).
- Enforce strict style compliance in both sampling and validation layers to guarantee filter correctness.

Assumptions honored (user-confirmed):
- Styles catalog auto‑updates from our GitHub address and is reachable.
- Cap the number of selected styles; document tradeoffs.
- Strict matching is the default; a hidden relax toggle may exist but remains OFF by default.


## Current State (Codebase Snapshot)

- Prompt building and token budgets
  - `Brainarr.Plugin/Services/LibraryAwarePromptBuilder.cs` uses static strategy/provider limits via `GetTokenLimitForStrategy` and then performs a one‑pass trim for overshoot.
  - Album listing is flat with simple 5‑item chunking; no style gating or grouping by artist.

- Iteration control
  - `IterativeRecommendationStrategy` slightly boosts iteration counts for `Comprehensive` but is not style‑aware.

- Settings and UI plumbing
  - `BrainarrSettings` already uses `FieldDefinition` and TagSelect for other features (Review, Observability).
  - `BrainarrOrchestrator.HandleAction` supports TagSelect options endpoints (e.g., `review/getoptions`, `observability/getoptions`) — a good pattern to reuse for styles.

- Library analysis and validation
  - `LibraryAnalyzer` extracts genres and rich metadata; no notion of normalized “styles”.
  - Validation (`Services/Validation`) does not enforce style filters.
  - Provider capability detection (`IProviderCapabilities`, `ModelDetectionService`) exists but is not integrated with prompt token ceilings.


## Key Challenges in the Original Plan

- Large flat catalog alone is not enough: without a library‑aware index, matching and coverage checks are slower and harder to tune for UX.
- Hardcoding model token ceilings in the prompt builder duplicates concerns and drifts from detected capabilities.
- Strict style filters must be enforced in multiple places (sampling, prompt instructions, and validator) to be truly reliable.
- Typeahead UX should bias toward a user’s library (coverage) rather than purely alphabetic/global matches.


## Proposed Architecture

### 1) Style Catalog + Library Style Index

- Files
  - `Brainarr.Plugin/Resources/music_styles.json` (embedded default) — shape per entry:
    - `{ "name": "Progressive Rock", "aliases": ["Prog Rock","Prog"], "slug": "progressive-rock", "parents": ["Rock"] }`.
  - `Brainarr.Plugin/Services/Styles/StyleCatalogService.cs`
  - `Brainarr.Plugin/Services/Styles/StyleIndex.cs`
  - `Brainarr.Plugin/Services/Styles/Style.cs` (DTO with `Name`, `Aliases`, `Slug`, `Parents`)

- Constants (extend `Brainarr.Plugin/Configuration/Constants.cs`)
  - `public const string StylesCatalogUrl`
  - `public const int StylesCatalogRefreshHours = 24`
  - `public const int StylesCatalogTimeoutMs = 5000`

- StyleCatalogService (singleton; DI via existing container)
  - Startup: load embedded JSON; validate; cache as `IReadOnlyList<Style>`; keep last‑good snapshot.
  - Auto‑update: fetch from `StylesCatalogUrl` with ETag (`If-None-Match`), timeout from `StylesCatalogTimeoutMs`. Schedule refresh every `StylesCatalogRefreshHours` using a lightweight `Timer` (pattern exists under `Services/Telemetry` and `Core/ConcurrentCache`).
  - APIs:
    - `IReadOnlyList<Style> GetAll()`
    - `IEnumerable<Style> Search(string query, int limit = 50)` — case/diacritic‑insensitive; match slug, name, aliases; return canonicals first.
    - `ISet<string> Normalize(IEnumerable<string> selected)` — alias to canonical slug (lowercase, kebab‑case).
    - `bool IsMatch(ICollection<string> genresOrTags, ISet<string> selectedSlugs)` — checks aliases and parents.
  - Fetching: use existing `IHttpClient` and `SecureJsonSerializer` patterns for safety; log a single warning per outage window; always fall back to embedded.

- StyleIndex (rebuilt on every run, cheap)
  - Scans `ILibraryAnalyzer.GetAllArtists()` and `GetAllAlbums()`; maps each item’s `Genres` to catalog slugs.
  - Maintains inverted indexes:
    - `slug -> HashSet<artistId>` and `slug -> HashSet<albumId>` for fast prefiltering.
  - Provides coverage metrics for UX: how many library artists/albums match selected styles.
  - API:
    - `PrefilterByStyles(List<Artist>, List<Album>, ISet<string> slugs)` → `(filteredArtists, filteredAlbums, coverage)`


### 2) Settings + UI: TagSelect for Music Styles

- `Brainarr.Plugin/BrainarrSettings.cs`
  - Add:
    - `IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();`
    - `[FieldDefinition(Label = "Music Styles", Type = FieldType.TagSelect, SelectOptionsProviderAction = "styles/getoptions", HelpText = "Select styles. Leave empty to use your library profile.")]`
    - Hidden maintainer fields:
      - `int MaxSelectedStyles { get; set; } = 10;` `[Hidden = HiddenType.Hidden, Advanced = true]`
      - `int? ComprehensiveTokenBudgetOverride { get; set; }` `[Hidden, Advanced]`
      - `bool RelaxStyleMatching { get; set; } = false;` `[Hidden, Advanced]`

- `Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs`
  - `HandleAction` → add:
    - `"styles/getoptions"` → returns `{ options: [{ value: "progressive-rock", name: "Progressive Rock", coverage: "42 artists" }, ...] }`
      - Backed by `StyleCatalogService.Search(query)`.
      - If no `query`, return top suggestions by library coverage (using `StyleIndex`).
      - Limit 50; debounce is handled client‑side by Lidarr UI.
  - Optional: `"styles/preview"` → `{ artists: n, albums: m }` for selected slugs; used to show immediate impact.

- Selection cap logic (applied server‑side)
  - If `StyleFilters.Count > MaxSelectedStyles`, keep the subset with highest library coverage then alphabetical; log a warning with before/after counts.


### 3) Style‑Aware Sampling and Strict Filtering

- `LibraryAwarePromptBuilder` integration points
  - Before sampling, if `StyleFilters` non‑empty:
    - Normalize to slugs via `StyleCatalogService.Normalize`.
    - Build `StyleIndex` and prefilter `allArtists`/`allAlbums` to matches.
    - Strict mode (default): do not widen beyond selected slugs. If coverage is low, proceed and log `low coverage`.
  - Keep existing behavior when empty, but continue emphasizing library context and duplicate avoidance.

- Prompt changes (when styles selected)
  - Add block near top:
    - `STYLE FILTERS: Progressive Rock, Art Rock (aliases recognized: Prog Rock, Prog)`
    - Rule: “Return items that belong to these styles only. Do not recommend outside these styles.”
  - List compression improvements:
    - Group albums by artist: `Artist — [Album1; Album2; …]`.
    - Chunk groups to ~5–7 per line; elide with “+ N more …”.

- Validation guardrail
  - Add a lightweight `StyleGuard` in `Services/Validation` that drops items whose `genre` cannot be normalized into the selected slugs (uses catalog aliases/parents). This ensures strict compliance even if the model drifts.


### 4) Token Budget Controller (Model‑Aware)

- Rationale: Avoid duplicating budget logic inside `LibraryAwarePromptBuilder` and use real provider/model signals.

- New: `Brainarr.Plugin/Services/Core/TokenBudgetService.cs`
  - Inputs: `SamplingStrategy`, `AIProvider`, `settings.ModelSelection` (or `ManualModelId`), optional `ComprehensiveTokenBudgetOverride`.
  - Uses `IProviderCapabilities` + curated model window hints to set ceilings:
    - OpenAI o4‑mini/4o‑mini: 64k (conservative)
    - Anthropic Claude 3.7 family: 120k
    - Groq Llama‑3.1‑70B: 32k
    - Qwen/Llama local: 32k default unless detected higher via provider capabilities
    - Unknown: fallback 20k
  - Returns an `EffectivePromptBudget` (input tokens) and `SafetyMargin` adjustments for local providers (keep 20% slack by default).

- `LibraryAwarePromptBuilder.GetEffectiveTokenLimit` delegates to `TokenBudgetService`.
  - Build pass → estimate → compress → re‑estimate → if still overshoot, trim albums, then artists, then optional metadata blocks.


### 5) Iterative Strategy Adjustments

- In `IterativeRecommendationStrategy`:
  - If `SamplingStrategy == Comprehensive` and `StyleFilters` non‑empty, add +1–2 iterations within existing caps to compensate for narrower candidate pool.
  - Keep duplication/library de‑dup logic unchanged.


### 6) Telemetry & Guardrails

- Log per run:
  - `SampledArtists`, `SampledAlbums`, `EstimatedTokens`, `AppliedStyleCount`, `StyleCoverageArtists/Albums`, `Budget`, `Trimmed=true/false`.
- Warnings:
  - Too many selected styles (beyond cap) and low coverage.
- Sanitize any user‑provided terms for prompts (reuse `InputSanitizer`).


## Implementation Blueprint

### Data Models

```csharp
// Brainarr.Plugin/Services/Styles/Style.cs
public sealed class Style
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty; // lowercase, kebab
    public List<string> Aliases { get; set; } = new();
    public List<string> Parents { get; set; } = new();
}
```

### Services

```csharp
// Brainarr.Plugin/Services/Styles/IStyleCatalogService.cs
public interface IStyleCatalogService
{
    IReadOnlyList<Style> GetAll();
    IEnumerable<Style> Search(string query, int limit = 50);
    ISet<string> Normalize(IEnumerable<string> selected);
    bool IsMatch(ICollection<string> genres, ISet<string> selectedSlugs);
}

// Brainarr.Plugin/Services/Styles/StyleCatalogService.cs (sketch)
public class StyleCatalogService : IStyleCatalogService
{
    // ctor(IHttpClient, Logger) — load embedded, schedule refresh, keep last-good
    // Use ETag with StylesCatalogUrl; SecureJsonSerializer for JSON; log once per outage window.
}

// Brainarr.Plugin/Services/Styles/StyleIndex.cs
public sealed class StyleIndex
{
    public StyleIndex(IStyleCatalogService catalog, Logger logger) { /* build inverted indexes from library */ }
    public (List<Artist> Artists, List<Album> Albums, (int artistMatches, int albumMatches) Coverage)
        PrefilterByStyles(List<Artist> artists, List<Album> albums, ISet<string> slugs) { /* fast match */ }
}
```

### Settings and UI

```csharp
// Brainarr.Plugin/BrainarrSettings.cs
[FieldDefinition(Label = "Music Styles", Type = FieldType.TagSelect, SelectOptionsProviderAction = "styles/getoptions", HelpText = "Select styles (aliases supported). Leave empty to use your library profile.")]
public IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();

[FieldDefinition(Label = "Max Selected Styles", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden)]
public int MaxSelectedStyles { get; set; } = 10;

[FieldDefinition(Label = "Comprehensive Token Budget Override", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden)]
public int? ComprehensiveTokenBudgetOverride { get; set; }

[FieldDefinition(Label = "Relax Style Matching", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden)]
public bool RelaxStyleMatching { get; set; } = false;
```

```csharp
// Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs
case "styles/getoptions":
    return GetStyleOptions(query, settings);

private object GetStyleOptions(IDictionary<string,string> query, BrainarrSettings settings)
{
    var q = query.TryGetValue("query", out var s) ? s : null;
    var catalog = _styleCatalog; // injected
    var all = string.IsNullOrWhiteSpace(q)
        ? SuggestTopByCoverage(settings)
        : catalog.Search(q, 50);

    var options = all.Select(x => new { value = x.Slug, name = x.Name, coverage = GetCoverageLabel(x.Slug) });
    return new { options };
}
```

### Wiring

- Default construction path (`Brainarr.Plugin/BrainarrImportList.cs`):
  - Instantiate and inject `StyleCatalogService` into `BrainarrOrchestrator` and/or `LibraryAwarePromptBuilder` depending on where normalization is applied.
  - If `StyleIndex` is constructed per run, create it inside orchestrator methods that already fetch `allArtists`/`allAlbums`.
  - Add `TokenBudgetService` to the same default construction path and update `LibraryAwarePromptBuilder` to use it.

### Prompt Assembly (styles selected)

```csharp
// LibraryAwarePromptBuilder.BuildLibraryAwarePromptWithMetrics(...)
if (settings.StyleFilters?.Any() == true)
{
    var slugs = _styleCatalog.Normalize(settings.StyleFilters);
    var index = new StyleIndex(_styleCatalog, _logger);
    var (fa, fb, cov) = index.PrefilterByStyles(allArtists, allAlbums, slugs);
    // strict: do not widen beyond matched fa/fb
    // warnings on low coverage
    sample = BuildSmartLibrarySample(fa, fb, availableTokens, settings.DiscoveryMode);
    styleBlock = MakeStyleBlock(slugs);
}
```

### Token Budgets

```csharp
// Brainarr.Plugin/Services/Core/TokenBudgetService.cs
public sealed class TokenBudgetService
{
    public int GetLimit(SamplingStrategy strat, AIProvider provider, string? modelId, int? overrideBudget) { /* model-aware */ }
}

// LibraryAwarePromptBuilder: replace GetTokenLimitForStrategy with TokenBudgetService
```

### Validation Guardrail

```csharp
// Brainarr.Plugin/Services/Validation/StyleGuard.cs
public sealed class StyleGuard
{
    public bool IsAllowed(Recommendation r, ISet<string> slugs) { /* normalize r.Genre against catalog; parents/aliases honored */ }
}

// Integrate in RecommendationPipeline before enrichment: drop outside-style items when filters selected.
```


## UX Improvements

- TagSelect shows:
  - Top N styles in your library (by coverage) when query is empty.
  - Per-option coverage hint: “42 artists” or “68 albums”.
  - Warning if cap exceeded: “Using top 10 by coverage; 4 ignored”.
- Quick presets (bundles): offer curated multi-style presets (e.g., “Post‑Rock + Ambient”, “Indie + Shoegaze”). Implement as static groups resolved to slugs.
- Optional “Preview” action shows how many library items will be used as context with current selections.


## Tests

- Unit tests
  - Style normalization (aliases, parents, case) and strict matching.
  - Catalog auto‑update: uses ETag, falls back to embedded on error.
  - Options provider returns library‑relevant suggestions when no query; handles empty query and query matches.
  - Prompt includes `STYLE FILTERS` block when styles selected; absent otherwise.
  - Token budget service returns larger limits for known models; override respected; local providers keep 20% safety margin.
  - Sampling respects strict style filter; warns on low coverage.

- Integration tests
  - Synthetic libraries with style annotations to validate coverage, budget compliance, and strict enforcement.


## Security and Reliability

- Sanitize all user strings before prompt insertion (`Services/Security/InputSanitizer`).
- Validate remote JSON schema and handle malformed data safely (`SecureJsonSerializer`).
- Single‑warning policy per outage window for remote catalog fetch; continue with embedded snapshot.


## Rollout

- PR1: Catalog + index + settings field + `styles/getoptions` + budget service + unit tests.
- PR2: Style‑aware sampling + prompt changes + validator guardrail + compression + integration tests + docs/help text.


## Definition of Done

- Music Styles TagSelect appears, persists, and shows coverage‑aware options.
- With styles selected: prompt includes style block; sampling and validator enforce strict matching.
- Without styles: prompt remains library‑centric and avoids generic content.
- Comprehensive mode uses model‑aware token budgets with compression and trimming.
- Telemetry includes style and budget metrics; tests pass.


## Notes on CI and Packaging

- Embed `music_styles.json` via `EmbeddedResource` in the csproj; ensure it’s included in the plugin artifact.
- No network requirement for unit tests; mock catalog and HTTP where applicable.
