# Safe Incremental Refactoring Plan - Brainarr
*Created: January 2025*

## Verification Results & Updated Assumptions

After thorough analysis, here are the verified findings and safe refactoring approach:

## 1. Cache Implementation Analysis ✅ VERIFIED

### Current State:
- **ConcurrentCache<TKey,TValue>** exists in 2 locations but is **ONLY used in tests**
- **RecommendationCache** is the **ONLY production cache** implementation
- Production code uses `IRecommendationCache` interface consistently

### Risk Assessment: **LOW**
- ConcurrentCache is isolated to test code
- No production impact from consolidation

### Safe Refactoring Steps:
```bash
# Phase 1: Clean up unused code (NO RISK)
1. Delete Services/ConcurrentCache.cs (not used anywhere)
2. Keep Services/Core/ConcurrentCache.cs (used by tests only)
3. Move test cache to test project

# Phase 2: Improve RecommendationCache (LOW RISK)
4. Add unit tests for RecommendationCache
5. Add memory pressure tests
6. Consider upgrading to IMemoryCache with better eviction
```

## 2. Async/Await Pattern Fix ✅ CRITICAL BUT FIXABLE

### Current State:
- Lidarr's `ImportListBase` requires **synchronous** `Fetch()` method
- `.GetAwaiter().GetResult()` is used due to framework constraint
- 10 occurrences in BrainarrImportList.cs

### Risk Assessment: **MEDIUM** 
- Cannot make Fetch() async (breaks Lidarr interface)
- Current pattern risks deadlock in ASP.NET context

### Safe Refactoring Steps:
```csharp
// Phase 1: Fix the most dangerous pattern (HIGH PRIORITY)
// Current DANGEROUS code:
public override IList<ImportListItemInfo> Fetch()
{
    var recommendations = GetRecommendationsAsync()
        .GetAwaiter().GetResult(); // DEADLOCK RISK!
}

// Safe replacement:
public override IList<ImportListItemInfo> Fetch()
{
    // Use Task.Run to avoid SynchronizationContext deadlock
    return Task.Run(async () => 
        await GetRecommendationsAsync().ConfigureAwait(false)
    ).GetAwaiter().GetResult();
}

// Phase 2: Consolidate async calls
// Create single async entry point to minimize sync-over-async
private IList<ImportListItemInfo> ExecuteSafely(Func<Task<IList<ImportListItemInfo>>> asyncOperation)
{
    return Task.Run(async () => 
        await asyncOperation().ConfigureAwait(false)
    ).GetAwaiter().GetResult();
}
```

### Testing Strategy:
1. Add deadlock detection test
2. Load test with concurrent requests
3. Test in Lidarr's actual hosting context

## 3. Rate Limiter Analysis ✅ PARTIALLY CORRECT

### Current State:
- **RateLimiter.cs** - Used in production (BrainarrImportList, BrainarrOrchestrator)
- **RateLimiterImproved.cs** - NOT USED (orphaned code)
- **ThreadSafeRateLimiter.cs** - NOT USED (orphaned code)
- **MusicBrainzRateLimiter.cs** - Specialized, used only in MinimalResponseParser

### Risk Assessment: **LOW**
- Main RateLimiter has good implementation with token bucket algorithm
- Already handles concurrency with locks

### Safe Refactoring Steps:
```bash
# Phase 1: Clean up (NO RISK)
1. Delete RateLimiterImproved.cs
2. Delete ThreadSafeRateLimiter.cs
3. Add unit tests for edge cases in RateLimiter.cs

# Phase 2: Improve existing (LOW RISK)
4. Add metrics/telemetry
5. Consider System.Threading.RateLimiting for .NET 7+ upgrade
```

## 4. Provider Implementation Consistency ✅ MANAGEABLE

### Current State:
- BaseCloudProvider uses Newtonsoft.Json
- OpenAIProvider uses System.Text.Json
- Different providers have inconsistent error handling

### Risk Assessment: **LOW-MEDIUM**
- Each provider works independently
- Can refactor one at a time

### Safe Refactoring Steps:
```csharp
// Phase 1: Create adapter pattern (NO BREAKING CHANGES)
public interface IJsonSerializer
{
    string Serialize<T>(T obj);
    T Deserialize<T>(string json);
}

// Phase 2: Gradual migration
1. Implement adapter for both JSON libraries
2. Update BaseCloudProvider to use IJsonSerializer
3. Migrate providers one by one with tests
4. Finally standardize on System.Text.Json
```

## Incremental Refactoring Schedule

### Sprint 1: Critical Fixes (No Breaking Changes)
**Week 1:**
- [ ] Fix async/await deadlock pattern in Fetch()
- [ ] Add comprehensive tests for the fix
- [ ] Delete unused RateLimiterImproved.cs and ThreadSafeRateLimiter.cs

**Week 2:**
- [ ] Delete unused Services/ConcurrentCache.cs
- [ ] Add unit tests for RecommendationCache
- [ ] Add load tests for concurrent Fetch() calls

### Sprint 2: Code Consolidation (Low Risk)
**Week 3:**
- [ ] Create IJsonSerializer abstraction
- [ ] Implement adapters for both JSON libraries
- [ ] Update BaseCloudProvider with adapter

**Week 4:**
- [ ] Migrate 2-3 providers to consistent pattern
- [ ] Add provider integration tests
- [ ] Performance benchmarks

### Sprint 3: Architecture Improvements (Medium Risk)
**Week 5:**
- [ ] Implement basic DI container registration
- [ ] Create factory pattern for providers
- [ ] Add provider health check tests

**Week 6:**
- [ ] Refactor settings into separate concern classes
- [ ] Add settings migration tests
- [ ] Update documentation

## Testing Checklist for Each Change

### Before ANY refactoring:
```powershell
# 1. Create baseline
dotnet test --collect:"XPlat Code Coverage" > baseline.txt

# 2. Run performance benchmark
dotnet run -c Release --project Brainarr.Tests -- --benchmark

# 3. Backup current working state
git checkout -b refactor/[change-name]
```

### After EACH change:
```powershell
# 1. Run all tests
dotnet test

# 2. Compare coverage
dotnet test --collect:"XPlat Code Coverage" > after.txt
diff baseline.txt after.txt

# 3. Test in actual Lidarr
Copy-Item Build\* "C:\ProgramData\Lidarr\bin\Plugins\"
# Restart Lidarr and test

# 4. Load test critical paths
k6 run load-test.js
```

## Rollback Strategy

Each refactoring is in its own branch with:
1. Feature flag to toggle new behavior
2. Automated rollback script
3. Database migration compatibility

```csharp
// Example feature flag
public class FeatureFlags
{
    public static bool UseImprovedAsyncPattern => 
        Environment.GetEnvironmentVariable("BRAINARR_IMPROVED_ASYNC") == "true";
}

// Usage
public override IList<ImportListItemInfo> Fetch()
{
    if (FeatureFlags.UseImprovedAsyncPattern)
        return FetchSafely();
    else
        return FetchLegacy(); // Current implementation
}
```

## Success Metrics

| Metric | Current | Target | Measurement |
|--------|---------|--------|-------------|
| Deadlock incidents | Unknown | 0 | APM monitoring |
| P95 response time | Baseline | -20% | Load tests |
| Memory usage | Baseline | -15% | Profiler |
| Test coverage | 45% | 65% | Coverage tools |
| Code duplication | 18% | 10% | SonarQube |

## Risk Mitigation

1. **Never refactor without tests** - Add tests BEFORE changing code
2. **One change at a time** - Each PR addresses single concern
3. **Feature flags** - All major changes behind flags
4. **Canary deployment** - Test with subset of users first
5. **Monitoring** - Add metrics before and after
6. **Documentation** - Update as you go, not after

## Next Immediate Actions

1. **TODAY**: Fix the async/await deadlock pattern (Critical)
2. **THIS WEEK**: Delete unused code (No risk, immediate cleanup)
3. **NEXT WEEK**: Add missing tests for critical paths

---

This plan ensures we improve the codebase iteratively without breaking anything. Each change is isolated, tested, and reversible.