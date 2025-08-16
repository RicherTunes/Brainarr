# Brainarr Tech Debt & Improvement Tasks

This document tracks technical debt, improvements, and optimization opportunities for the Brainarr Lidarr plugin. The codebase is production-ready (v1.0.0) with excellent architecture - these are enhancement opportunities.

**Overall Assessment: 8.5/10** - Excellent architecture with room for optimization

## 🚨 Critical Priority (Security & Stability)

### 1. Fix Synchronous-over-Async Deadlock Risk
**File**: `Brainarr.Plugin/BrainarrImportList.cs` (lines 88, 232, 620, 675, 997)  
**Issue**: `Task.Run(async () => await method()).GetAwaiter().GetResult()` patterns  
**Risk**: Thread pool deadlocks, Lidarr freeze  
**Solution**: 
- Implement safer sync-over-async patterns or AsyncHelper.RunSync()
- Add timeout mechanisms to prevent indefinite blocking
- Consider async refactoring where possible

```csharp
// Current (risky):
return Task.Run(async () => await FetchAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

// Better:
return AsyncHelper.RunSync(() => FetchAsync());
// Or implement proper async path
```

### 2. Sanitize Response Content in Error Logs
**File**: `Brainarr.Plugin/Services/Support/ErrorHandling.cs` (line 100)  
**Issue**: API keys and sensitive data may leak in error response logs  
**Risk**: Security vulnerability, credential exposure  
**Solution**: Implement response content sanitization to remove API keys, tokens, and sensitive data

```csharp
// Current:
logger.Error($"{providerName}: {(int)statusCode} {statusCode} - {responseContent?.Substring(0, Math.Min(200, responseContent.Length))}");

// Better:
var sanitizedContent = SanitizeResponseContent(responseContent);
logger.Error($"{providerName}: {(int)statusCode} {statusCode} - {sanitizedContent}");
```

### 3. Implement Comprehensive Input Validation
**Files**: Multiple provider classes  
**Issue**: User inputs (genres, prompts) not properly sanitized before AI provider calls  
**Risk**: Injection attacks, malformed requests  
**Solution**: 
- Add input validation for user prompts
- Sanitize genre inputs
- Implement request size limits
- Add character filtering for special characters

## ⚡ High Priority (Performance)

### 4. Fix Rate Limiter Thread Blocking
**File**: `Brainarr.Plugin/Services/RateLimiter.cs` (line 100)  
**Issue**: `Thread.Sleep()` in async context blocks thread pool threads  
**Impact**: Poor performance, thread pool starvation  
**Solution**: Replace with `await Task.Delay(waitTime)` and make method fully async

```csharp
// Current:
Thread.Sleep(waitTime);

// Better:
await Task.Delay(waitTime, cancellationToken);
```

### 5. Optimize Cache Cleanup Performance
**File**: `Brainarr.Plugin/Services/RecommendationCache.cs` (lines 128-131)  
**Issue**: Full dictionary enumeration for cleanup operations (O(n))  
**Impact**: Performance degradation with large caches  
**Solution**: Implement timer-based cleanup or priority queue for O(1) operations

### 6. Implement Size-Limited Model Caching
**File**: `Brainarr.Plugin/BrainarrImportList.cs` (line 39)  
**Issue**: Static concurrent dictionary without size limits or cleanup  
**Impact**: Potential memory leaks in long-running instances  
**Solution**: Implement LRU cache with size limits and TTL

## 🔧 Medium Priority (Code Quality)

### 7. Refactor Large Methods
**File**: `Brainarr.Plugin/BrainarrImportList.cs`  
**Issue**: Methods exceed 50-100 lines (FetchAsync ~110 lines, RequestAction ~70 lines)  
**Impact**: Maintainability, testability  
**Solution**: Extract smaller, focused methods following Single Responsibility Principle

### 8. Consolidate Magic Numbers
**Files**: Multiple  
**Issue**: Hardcoded values scattered throughout code  
**Examples**: 
- Line 258: `preferredModels` array
- Various timeout values
**Solution**: Move all magic numbers to `BrainarrConstants.cs`

### 9. Standardize Error Handling
**Files**: Various provider implementations  
**Issue**: Inconsistent error handling patterns across providers  
**Solution**: Ensure all providers use `ErrorHandling.HandleProviderError()` consistently

## 🏗️ Architecture Improvements (Future)

### 10. Implement Proper Dependency Injection
**File**: `Brainarr.Plugin/BrainarrImportList.cs` (lines 64-77)  
**Issue**: Manual service instantiation instead of DI container  
**Impact**: Testability, lifecycle management  
**Solution**: Implement proper DI container registration

### 11. Interface Segregation
**Files**: Various service interfaces  
**Issue**: Some interfaces too broad (e.g., `IAIService` multiple responsibilities)  
**Solution**: Split large interfaces following Interface Segregation Principle

### 12. Enhanced API Key Validation
**Files**: Provider classes  
**Issue**: API keys validated only for presence, not format  
**Solution**: Add format validation for each provider's API key pattern

## 🔒 Security Enhancements

### 13. Request/Response Logging Security
**Issue**: Potential API keys in debug logs  
**Solution**: Implement request/response logging with automatic key redaction

### 14. Input Sanitization Framework
**Issue**: Need comprehensive input sanitization  
**Solution**: Create reusable sanitization framework for all user inputs

## 📚 Configuration & Maintainability

### 15. Project Configuration Cleanup
**File**: `Brainarr.Plugin/Brainarr.Plugin.csproj`  
**Issues**:
- Extensive warning suppression (NoWarn)
- Nullable disabled globally
**Solution**: 
- Enable nullable reference types selectively
- Address warnings instead of suppressing
- Enable stricter analysis rules

### 16. Expand Documentation
**Issue**: Some complex algorithms lack XML documentation  
**Solution**: Add comprehensive XML docs for public APIs

## 🧪 Testing Improvements

### 17. Integration Test Coverage
**Issue**: Limited integration tests for provider interactions  
**Solution**: Expand integration test suite for end-to-end scenarios

### 18. Performance Test Suite
**Issue**: No dedicated performance/load testing  
**Solution**: Add performance benchmarks for caching, rate limiting, and provider calls

## 📅 Implementation Phases

### Phase 1: Critical Security & Stability (Immediate)
- [ ] Task #1: Fix synchronous-over-async patterns
- [ ] Task #2: Implement response content sanitization  
- [ ] Task #3: Add comprehensive input validation

### Phase 2: Performance Optimization (Week 1-2)
- [ ] Task #4: Fix rate limiter blocking operations
- [ ] Task #5: Optimize cache cleanup mechanisms
- [ ] Task #6: Implement size-limited model caching

### Phase 3: Code Quality (Week 3-4)
- [ ] Task #7: Refactor large methods
- [ ] Task #8: Consolidate magic numbers
- [ ] Task #9: Standardize error handling

### Phase 4: Architecture Improvements (Month 2)
- [ ] Task #10: Implement proper dependency injection
- [ ] Task #11: Refactor interfaces for better separation
- [ ] Task #12: Enhance API key validation

### Phase 5: Long-term Enhancements (Month 3+)
- [ ] Task #13-18: Security, configuration, and testing improvements

## 🌟 Positive Highlights

The codebase demonstrates exceptional qualities:
- ✅ Comprehensive provider support (9 AI providers)
- ✅ Robust error handling with categorized exceptions
- ✅ Excellent caching strategy with intelligent invalidation
- ✅ Thorough testing suite (30+ test files)
- ✅ Production-ready security with proper API key handling
- ✅ Advanced features (health monitoring, rate limiting, retry policies)
- ✅ Clean architecture with proper separation of concerns
- ✅ Extensive configuration options with intelligent defaults

## 📝 Notes

- All tasks are enhancements to an already production-ready codebase
- Priority should be given to critical security and stability issues
- Performance optimizations can be implemented incrementally
- Architecture improvements are long-term goals for maintainability

---

**Last Updated**: August 15, 2025  
**Status**: Production-ready v1.0.0 with 100% CI success  
**Next Review**: After implementing Phase 1 critical tasks