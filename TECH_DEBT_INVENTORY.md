# Technical Debt Inventory & Refactoring Plan

## Executive Summary
The Brainarr codebase contains 9 files exceeding 500 lines with significant architectural violations. Total tech debt remediation effort: ~40 hours.

## Critical Issues (Priority 1 - Immediate Action Required)

### 1. Duplicate RecommendationValidator Files
- **Files**: 
  - `/Services/RecommendationValidator.cs` (531 lines)
  - `/Services/Validation/RecommendationValidator.cs` (503 lines)
- **Issue**: Two slightly different versions of the same validator
- **Risk**: High - Inconsistent validation logic, maintenance nightmare
- **Resolution**: Merge into single file in `/Services/Validation/` namespace
- **Effort**: 2 hours

### 2. ProviderResponses.cs Monolith (594 lines, 177 methods!)
- **Issue**: Single file containing ALL provider response models
- **Violations**: Single Responsibility, Open/Closed principles
- **Risk**: High - Changes affect all providers
- **Resolution**: Decompose into provider-specific response models
- **Effort**: 4 hours

## High Priority Issues (Priority 2 - This Sprint)

### 3. BrainarrSettings.cs Bloat (642 lines, 72 methods)
- **Issue**: Configuration class handling too many responsibilities
- **Violations**: Single Responsibility, Interface Segregation
- **Resolution**: Split into:
  - `CoreSettings.cs` - Core configuration
  - `ProviderSettings.cs` - Provider-specific settings
  - `ValidationSettings.cs` - Validation rules
  - `UISettings.cs` - UI field definitions
- **Effort**: 6 hours

### 4. BrainarrImportList.cs Complexity (778 lines, 37 methods)
- **Issue**: Main class mixing orchestration, business logic, and infrastructure
- **Resolution**: Extract to:
  - `ImportListOrchestrator.cs` - Main workflow
  - `ProviderManager.cs` - Provider lifecycle
  - `LibraryProfileBuilder.cs` - Library analysis
  - `RecommendationProcessor.cs` - Processing pipeline
- **Effort**: 8 hours

## Medium Priority Issues (Priority 3 - Next Sprint)

### 5. HallucinationDetector.cs (659 lines)
- **Issue**: Complex validation logic in single file
- **Resolution**: Split into strategy pattern:
  - `IDetectionStrategy` interface
  - `PatternDetectionStrategy.cs`
  - `StatisticalDetectionStrategy.cs`
  - `SemanticDetectionStrategy.cs`
- **Effort**: 6 hours

### 6. LocalAIProvider.cs (608 lines)
- **Issue**: Provider implementation too complex
- **Resolution**: Extract:
  - `LocalModelManager.cs` - Model lifecycle
  - `LocalProviderClient.cs` - HTTP communication
  - `LocalResponseParser.cs` - Response processing
- **Effort**: 5 hours

### 7. LibraryAnalyzer.cs (576 lines)
- **Issue**: Multiple analysis responsibilities
- **Resolution**: Decompose to:
  - `GenreAnalyzer.cs`
  - `ArtistProfiler.cs`
  - `ListeningPatternAnalyzer.cs`
- **Effort**: 5 hours

### 8. LibraryAwarePromptBuilder.cs (563 lines)
- **Issue**: Complex prompt generation logic
- **Resolution**: Template pattern:
  - `IPromptTemplate` interface
  - `RecommendationPromptTemplate.cs`
  - `ContextPromptTemplate.cs`
  - `PromptComposer.cs`
- **Effort**: 4 hours

## File Size & Complexity Metrics

| File | Lines | Methods | Complexity | Priority |
|------|-------|---------|------------|----------|
| ProviderResponses.cs | 594 | 177 | Very High | P1 |
| BrainarrSettings.cs | 642 | 72 | High | P2 |
| BrainarrImportList.cs | 778 | 37 | High | P2 |
| HallucinationDetector.cs | 659 | 28 | Medium | P3 |
| LocalAIProvider.cs | 608 | 31 | Medium | P3 |
| LibraryAnalyzer.cs | 576 | 24 | Medium | P3 |
| LibraryAwarePromptBuilder.cs | 563 | 22 | Medium | P3 |
| RecommendationValidator.cs (dup) | 531/503 | 26 | High | P1 |

## Dependency Analysis

### Core Dependencies
```
BrainarrImportList
├── AIProviderFactory
├── LibraryAnalyzer
├── IterativeRecommendationStrategy
├── RecommendationCache
├── ProviderHealthMonitor
└── ModelDetectionService
```

### Provider Dependencies
```
IAIProvider (interface)
├── BaseCloudProvider
│   ├── OpenAIProvider
│   ├── AnthropicProvider
│   ├── GeminiProvider
│   └── [Other Cloud Providers]
└── LocalAIProvider
    ├── OllamaProvider
    └── LMStudioProvider
```

## Refactoring Sequence

### Phase 1: Critical Fixes (Day 1)
1. Resolve duplicate RecommendationValidator
2. Begin ProviderResponses decomposition
3. Add integration tests for affected areas

### Phase 2: Core Refactoring (Days 2-3)
1. Decompose BrainarrSettings
2. Extract BrainarrImportList responsibilities
3. Update all provider configurations
4. Comprehensive testing

### Phase 3: Provider Optimization (Days 4-5)
1. Refactor LocalAIProvider
2. Apply provider base class patterns
3. Optimize provider factory
4. Performance testing

### Phase 4: Analysis & Validation (Days 6-7)
1. Decompose HallucinationDetector
2. Refactor LibraryAnalyzer
3. Optimize LibraryAwarePromptBuilder
4. Full regression testing

## Risk Mitigation

### Backward Compatibility
- All public APIs remain unchanged
- Internal refactoring only
- Incremental migration approach
- Feature flags for rollback

### Testing Strategy
- Unit tests for each new component
- Integration tests for workflows
- Performance benchmarks before/after
- Canary deployment approach

## Success Metrics

### Code Quality
- ✅ No files > 500 lines
- ✅ No classes > 300 lines
- ✅ Cyclomatic complexity < 10 per method
- ✅ Test coverage > 90%

### Performance
- ✅ No performance regression
- ✅ Memory usage stable or improved
- ✅ Response time maintained or improved
- ✅ Resource utilization optimized

### Maintainability
- ✅ Code duplication < 5%
- ✅ Clear separation of concerns
- ✅ SOLID principles adherence
- ✅ Comprehensive documentation

## Estimated Timeline
- **Total Effort**: 40 hours
- **Duration**: 7-10 days
- **Resources**: 1 senior developer
- **Review Cycles**: 3 checkpoints

## Next Actions
1. ✅ Review and approve inventory
2. ⏳ Begin Phase 1 critical fixes
3. ⏳ Set up monitoring for regression
4. ⏳ Create rollback procedures