# Brainarr Technical Debt Refactoring Plan

## Executive Summary
This document outlines a comprehensive refactoring strategy to eliminate technical debt in the Brainarr codebase, targeting a reduction from 15,162 lines to approximately 10,000 lines through intelligent decomposition and deduplication.

## Phase 1: Critical Decompositions (Week 1)

### 1.1 BrainarrImportList.cs Decomposition (711 → 150 lines)

**Current Issues:**
- Monolithic class violating SRP
- 82-line Fetch() method with 8+ responsibilities
- Mixed concerns: orchestration, caching, health, UI

**Target Architecture:**
```
BrainarrImportList.cs (150 lines) - Lidarr integration only
├── Services/Orchestration/
│   ├── ImportListOrchestrator.cs (120 lines)
│   ├── FetchContext.cs (30 lines)
│   └── FetchResult.cs (25 lines)
├── Services/Library/
│   ├── LibraryProfileService.cs (150 lines)
│   ├── LibraryStatistics.cs (40 lines)
│   └── ArtistProfileBuilder.cs (80 lines)
└── Services/Detection/
    ├── ModelDetectionCoordinator.cs (100 lines)
    └── ProviderCapabilityDetector.cs (60 lines)
```

**Refactoring Steps:**
1. Extract orchestration logic to ImportListOrchestrator
2. Move library profiling to LibraryProfileService
3. Extract model detection to ModelDetectionCoordinator
4. Create FetchContext for parameter passing
5. Implement unit tests for each extracted component

### 1.2 Provider Base Class Extraction (40% duplication → 5%)

**Current Issues:**
- 9 providers with 80% code duplication
- Repeated HTTP client setup, error handling, parsing
- No shared abstractions

**Target Architecture:**
```
Services/Providers/Base/
├── BaseHttpProvider.cs (200 lines)
├── BaseLocalProvider.cs (150 lines)
├── ProviderResponse.cs (30 lines)
├── ProviderError.cs (40 lines)
└── HttpClientFactory.cs (60 lines)

Services/Providers/ (reduced from ~350 to ~100 lines each)
├── OpenAIProvider.cs (100 lines)
├── AnthropicProvider.cs (90 lines)
├── GeminiProvider.cs (110 lines)
└── ... (other providers similarly reduced)
```

**Implementation Strategy:**
1. Create BaseHttpProvider with common HTTP logic
2. Extract error handling to ProviderError
3. Standardize response parsing in base class
4. Migrate providers to inherit from base
5. Remove duplicated code from each provider

### 1.3 BrainarrSettings.cs Refactoring (380 → 200 lines)

**Current Issues:**
- Complex switch statements in properties
- Mixed validation and UI concerns
- Poor extensibility

**Target Architecture:**
```
Configuration/
├── BrainarrSettings.cs (200 lines)
├── Settings/
│   ├── ProviderSettingsResolver.cs (80 lines)
│   ├── ConfigurationValidator.cs (100 lines)
│   └── SettingsFieldBuilder.cs (60 lines)
└── Strategies/
    ├── IProviderStrategy.cs (20 lines)
    ├── OpenAIStrategy.cs (40 lines)
    ├── AnthropicStrategy.cs (40 lines)
    └── ... (other provider strategies)
```

## Phase 2: Dependency Injection & Architecture (Week 2)

### 2.1 Implement Proper DI Container

**Current Constructor:**
```csharp
public BrainarrImportList(IHttpClient httpClient, ICacheManager cacheManager, ILidarrCloudRequestBuilder cloudRequestBuilder)
{
    // 8+ manual service instantiations
    _aiProviderFactory = new AIProviderFactory(httpClient);
    _libraryAnalyzer = new LibraryAnalyzer();
    // ...
}
```

**Target Constructor:**
```csharp
public BrainarrImportList(
    IImportListOrchestrator orchestrator,
    IProviderManager providerManager,
    ILibraryProfileService libraryService)
{
    _orchestrator = orchestrator;
    _providerManager = providerManager;
    _libraryService = libraryService;
}
```

### 2.2 Service Registration

```csharp
public static class ServiceRegistration
{
    public static void RegisterBrainarrServices(this IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<IImportListOrchestrator, ImportListOrchestrator>();
        services.AddSingleton<IProviderManager, ProviderManager>();
        services.AddSingleton<ILibraryProfileService, LibraryProfileService>();
        
        // Provider Services
        services.AddSingleton<IProviderFactory, AIProviderFactory>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        
        // Support Services
        services.AddSingleton<IRecommendationCache, RecommendationCache>();
        services.AddSingleton<IRateLimiter, RateLimiter>();
        services.AddSingleton<IProviderHealth, ProviderHealth>();
    }
}
```

## Phase 3: Test Coverage Enhancement (Week 3)

### 3.1 Unit Test Structure

```
Brainarr.Tests/
├── Unit/
│   ├── Orchestration/
│   │   ├── ImportListOrchestratorTests.cs
│   │   └── FetchContextTests.cs
│   ├── Library/
│   │   ├── LibraryProfileServiceTests.cs
│   │   └── ArtistProfileBuilderTests.cs
│   ├── Providers/Base/
│   │   ├── BaseHttpProviderTests.cs
│   │   └── HttpClientFactoryTests.cs
│   └── Configuration/
│       ├── ProviderSettingsResolverTests.cs
│       └── ConfigurationValidatorTests.cs
├── Integration/
│   ├── ProviderIntegrationTests.cs
│   ├── EndToEndWorkflowTests.cs
│   └── LibraryIntegrationTests.cs
└── Performance/
    ├── LoadTests.cs
    └── BenchmarkTests.cs
```

### 3.2 Coverage Targets

- **Unit Tests**: 95% coverage for all new components
- **Integration Tests**: 85% coverage for workflows
- **Edge Cases**: 100% coverage for error paths
- **Performance Tests**: Baseline benchmarks for all operations

## Phase 4: Performance Optimization (Week 4)

### 4.1 Caching Strategy

```csharp
public interface ICachingStrategy
{
    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, CacheOptions options);
    void Invalidate(string pattern);
}

public class SmartCachingStrategy : ICachingStrategy
{
    // Implement adaptive caching based on usage patterns
    // Use sliding expiration for frequently accessed items
    // Implement cache warming for predictable requests
}
```

### 4.2 Async/Await Optimization

- Convert all I/O operations to async
- Implement proper cancellation token support
- Use ValueTask for hot paths
- Implement async parallel processing for batch operations

## Migration Strategy

### Step 1: Create Feature Branch
```bash
git checkout -b refactor/technical-debt-elimination
```

### Step 2: Incremental Refactoring
1. Start with BaseHttpProvider extraction (low risk)
2. Refactor one provider at a time
3. Run full test suite after each change
4. Commit atomically with clear messages

### Step 3: Parallel Development
- Main branch continues receiving bug fixes
- Refactoring branch rebased daily
- Feature flags for gradual rollout

### Step 4: Validation Gates
- [ ] All existing tests pass
- [ ] New unit tests achieve 95% coverage
- [ ] Performance benchmarks show no regression
- [ ] Security scan passes
- [ ] Code review by senior architect
- [ ] Integration tests in staging environment

## Risk Mitigation

### Rollback Strategy
1. Feature flags for each major component
2. Canary deployment to 10% of users
3. Monitoring dashboard for error rates
4. Automated rollback on threshold breach

### Compatibility Maintenance
- Maintain backward compatibility for 2 releases
- Deprecation warnings for old patterns
- Migration guide for custom extensions

## Success Metrics

### Code Quality
- **Cyclomatic Complexity**: <10 per method
- **Method Length**: <30 lines average
- **Class Size**: <300 lines maximum
- **Test Coverage**: >90% overall

### Performance
- **API Response Time**: <100ms p95
- **Memory Usage**: <20% reduction
- **CPU Usage**: <15% reduction
- **Cache Hit Rate**: >85%

### Maintainability
- **Code Duplication**: <5%
- **Technical Debt Ratio**: <5%
- **Maintainability Index**: >70

## Timeline

**Week 1**: Critical decompositions (BrainarrImportList, Providers)
**Week 2**: DI implementation and architecture improvements
**Week 3**: Test coverage enhancement and validation
**Week 4**: Performance optimization and deployment prep
**Week 5**: Staging deployment and monitoring
**Week 6**: Production rollout with feature flags

## Expert Validation Required

Before implementation, the following expert consultations are required:

1. **Security Expert**: Validate authentication flows and data handling
2. **Performance Specialist**: Review async patterns and caching strategy
3. **Database Architect**: Validate query patterns and transactions
4. **API Designer**: Ensure contract compatibility
5. **DevOps Engineer**: Review deployment and monitoring strategy

## Appendix: File Size Projections

| Component | Current Lines | Target Lines | Reduction |
|-----------|--------------|--------------|-----------|
| BrainarrImportList.cs | 711 | 150 | 79% |
| BrainarrSettings.cs | 380 | 200 | 47% |
| Provider Classes (avg) | 350 | 100 | 71% |
| Total Codebase | 15,162 | ~10,000 | 34% |

## Next Steps

1. Get expert validation on architecture decisions
2. Create detailed task breakdown in project management tool
3. Set up monitoring dashboards
4. Begin with BaseHttpProvider extraction
5. Implement CI/CD pipeline changes for validation