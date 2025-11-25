# PR #131 Integration Plan: Comprehensive Music Styles

## Overview

PR #131 (Comprehensive Music Styles) was merged but had to be reverted due to interface incompatibilities. This document outlines the proper integration plan.

## Root Cause Analysis

The PR introduced code that was incompatible with main branch in several ways:

### 1. HttpClient Type Mismatch
- **Main branch**: Uses `NzbDrone.Common.Http.IHttpClient` (Lidarr's abstraction)
- **Feature branch**: Uses `System.Net.Http.HttpClient` (direct .NET)
- **Impact**: Constructor type mismatch in `StyleCatalogService`

### 2. IStyleCatalogService Interface Differences
- **Main branch has**: `ResolveSlug`, `GetBySlug`, `GetSimilarSlugs`
- **Feature branch has**: `RefreshAsync` (async refresh capability)
- **Feature branch missing**: The lookup methods above
- **Impact**: Interface method resolution failures

### 3. ResiliencePolicy Method Call Errors
- **Feature branch pattern** (WRONG):
  ```csharp
  ResiliencePolicy.WithHttpResilienceAsync(
      _ => _httpClient.ExecuteAsync(request),  // Lambda to first param
      origin: "openai", ...);
  ```
- **Correct pattern**:
  ```csharp
  ResiliencePolicy.WithHttpResilienceAsync(
      request,                                       // HttpRequest first
      (req, token) => _httpClient.ExecuteAsync(req), // send delegate second
      origin: "openai", ...);
  ```

### 4. Type System (StyleEntry vs Style)
- **Main branch**: `StyleEntry` class
- **Feature branch**: `Style` record
- **Impact**: Type resolution failures in prompting components

---

## Integration Steps

### Phase 1: Interface Reconciliation

**File**: `Brainarr.Plugin/Services/Styles/StyleCatalogService.cs`

Update `IStyleCatalogService` to include ALL methods:

```csharp
public interface IStyleCatalogService
{
    // From main branch (KEEP)
    IReadOnlyList<StyleEntry> GetAll();
    IEnumerable<StyleEntry> Search(string query, int limit = 50);
    ISet<string> Normalize(IEnumerable<string> selected);
    string? ResolveSlug(string value);
    StyleEntry? GetBySlug(string slug);
    IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug);

    // Modified (add relaxParentMatch parameter)
    bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs, bool relaxParentMatch = false);

    // From feature branch (ADD)
    Task RefreshAsync(CancellationToken token = default);
}
```

### Phase 2: Keep IHttpClient Abstraction

**CRITICAL**: Do NOT change to `System.Net.Http.HttpClient`

The `StyleCatalogService` constructor must remain:
```csharp
public StyleCatalogService(Logger logger, IHttpClient httpClient)
```

**Why**:
- Maintains Lidarr integration compatibility
- Allows mocking in tests
- Respects Lidarr's configuration (proxies, auth, retry logic)

### Phase 3: Fix ResiliencePolicy Calls in All Providers

**Files to update**:
- `Services/Providers/OpenAIProvider.cs`
- `Services/Providers/AnthropicProvider.cs`
- `Services/Providers/DeepSeekProvider.cs`
- `Services/Providers/GroqProvider.cs`
- `Services/Providers/OpenRouterProvider.cs`
- `Services/Providers/PerplexityProvider.cs`

**Pattern to fix** (search for `WithHttpResilienceAsync`):
```csharp
// WRONG (feature branch)
var response = await ResiliencePolicy.WithHttpResilienceAsync(
    _ => _httpClient.ExecuteAsync(request),
    origin: "provider", ...);

// CORRECT
var response = await ResiliencePolicy.WithHttpResilienceAsync(
    request,
    (req, token) => _httpClient.ExecuteAsync(req),
    origin: "provider", ...);
```

### Phase 4: Add Style Catalog to RecommendationPipeline

**File**: `Brainarr.Plugin/Services/Core/RecommendationPipeline.cs`

1. Add field:
   ```csharp
   private readonly IStyleCatalogService _styleCatalog;
   ```

2. Add to constructor:
   ```csharp
   public RecommendationPipeline(
       ...,
       IStyleCatalogService styleCatalog)
   {
       _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
   }
   ```

3. Add backward-compatible overload for existing tests

4. Add style filtering in `ProcessAsync`:
   ```csharp
   var selected = settings?.StyleFilters?.ToList() ?? new List<string>();
   if (selected.Count > 0)
   {
       var slugs = _styleCatalog.Normalize(selected);
       var relax = settings.RelaxStyleMatching;
       validated = validated
           .Where(r => _styleCatalog.IsMatch(r.LibraryGenres ?? new List<string>(), slugs, relax))
           .ToList();
   }
   ```

### Phase 5: Ensure Type Definitions

**File**: `Brainarr.Plugin/Services/Styles/Style.cs` (or StyleCatalogService.cs)

Ensure both exist:
```csharp
// Primary type used by interface
public class StyleEntry
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public List<string> Parents { get; set; } = new();
}

// For similarity scoring
public readonly struct StyleSimilarity
{
    public string Slug { get; init; }
    public double Score { get; init; }
}
```

### Phase 6: Update Tests

Update all tests that instantiate:
- `StyleCatalogService` → pass `IHttpClient` (mock or null)
- `RecommendationPipeline` → pass `IStyleCatalogService`

---

## Implementation Order

1. ✅ Interface update (add all methods)
2. ✅ Keep IHttpClient (do NOT change)
3. ✅ Fix ResiliencePolicy calls (all providers)
4. ✅ Add StyleEntry/StyleSimilarity types
5. ✅ Update RecommendationPipeline
6. ✅ Update factory registrations
7. ✅ Update/add tests
8. ✅ Integration test full build

---

## Validation Checklist

Before merging:
- [ ] `IStyleCatalogService` has all 8 methods
- [ ] `StyleCatalogService` uses `IHttpClient`
- [ ] All provider `WithHttpResilienceAsync` calls are correct
- [ ] `RecommendationPipeline` has `_styleCatalog` field
- [ ] Tests pass locally
- [ ] CI passes

---

## Files from Feature Branch to Integrate

### New Files:
- `Services/Core/TokenBudgetService.cs`
- `Services/Styles/Style.cs` (if using record)
- `catalog/music_styles.json`
- `docs/COMPREHENSIVE_SAMPLING_AND_STYLES_PLAN.md`
- `wiki-content/Music-Styles.md`

### New Tests:
- `Tests/Services/Core/OrchestratorStylesOptionsTests.cs`
- `Tests/Services/Core/RecommendationPipelineStyleGuardTests.cs`
- `Tests/Services/Core/TokenBudgetServiceTests.cs`
- `Tests/Services/LibraryAwarePromptBuilderStyleTests.cs`
- `Tests/Services/Styles/StyleCatalogDataTests.cs`
- `Tests/Services/Styles/StyleCatalogServiceTests.cs`

### Modified Files:
- `BrainarrSettings.cs` (StyleFilters, RelaxStyleMatching properties)
- `Resources/music_styles.json` (expanded catalog)
- Provider files (fix ResiliencePolicy calls)

---

## Decision Summary

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| HTTP Client | Keep `IHttpClient` | Lidarr compatibility, mockability |
| Type System | Keep `StyleEntry` class | Consistency with existing code |
| Interface | Merge both sets of methods | Feature completeness |
| Method Calls | Use main branch pattern | Correct parameter order |

---

## Contact

Created: 2025-11-25
Status: Ready for implementation
Related PR: #131 (feature/comprehensive-styles)
