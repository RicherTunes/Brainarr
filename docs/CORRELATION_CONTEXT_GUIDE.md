# CorrelationContext Developer Guide

## Overview

The `CorrelationContext` is a crucial component in Brainarr that provides request tracking and correlation across the entire plugin execution chain. It enables comprehensive distributed tracing, debugging, and performance monitoring by assigning unique identifiers to each request flow.

## Core Concepts

### What is Correlation Context?

Correlation context is a pattern for tracking related operations across different components and threads in a distributed system. Each logical operation (like a recommendation request) gets a unique correlation ID that follows it through:

- Multiple service calls
- Provider interactions
- Cache operations
- Log entries
- Error handling

### Why Use Correlation Context?

1. **Debugging**: Trace a single request through multiple components
2. **Performance Analysis**: Measure end-to-end request timing
3. **Error Tracking**: Link errors to specific request contexts
4. **Audit Trail**: Create comprehensive logs for compliance
5. **Distributed Tracing**: Connect operations across service boundaries

## Architecture

### Class Structure

```
CorrelationContext (static)
├── Thread-local storage for correlation IDs
├── Automatic generation of unique IDs
└── Context management methods

CorrelationScope (IDisposable)
├── Scoped correlation management
├── Automatic context restoration
└── Nested scope support

LoggerExtensions (static)
├── Correlation-aware logging methods
└── Formatted log output with correlation IDs

UrlSanitizer (static)
├── URL sanitization for secure logging
└── API key masking
└── Query parameter removal
```

## Usage Patterns

### Basic Usage

#### Starting a New Correlation Context

```csharp
// Method 1: Using StartNew()
var correlationId = CorrelationContext.StartNew();
_logger.InfoWithCorrelation($"Starting operation");

// Method 2: Using Current property (auto-generates if needed)
var correlationId = CorrelationContext.Current;
_logger.DebugWithCorrelation($"Current correlation: {correlationId}");

// Method 3: Setting explicit correlation ID
CorrelationContext.Current = "external-correlation-123";
```

#### Using Correlation Scopes

```csharp
// Automatic scope management with using statement
using (var scope = new CorrelationScope())
{
    _logger.InfoWithCorrelation("Starting scoped operation");
    
    // All operations within this scope share the same correlation ID
    await ProcessRecommendations();
    await UpdateCache();
    
    _logger.InfoWithCorrelation("Completed scoped operation");
}
// Previous correlation context automatically restored
```

#### Nested Scopes

```csharp
var outerCorrelation = CorrelationContext.StartNew();
_logger.InfoWithCorrelation("Outer operation"); // Uses outer correlation

using (var innerScope = new CorrelationScope())
{
    _logger.InfoWithCorrelation("Inner operation"); // Uses inner correlation
    
    using (var deepScope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation("Deep operation"); // Uses deep correlation
    }
    
    _logger.InfoWithCorrelation("Back to inner"); // Back to inner correlation
}

_logger.InfoWithCorrelation("Back to outer"); // Back to outer correlation
```

### Advanced Patterns

#### Cross-Thread Correlation

```csharp
public async Task ProcessAsync()
{
    var correlationId = CorrelationContext.Current;
    
    await Task.Run(() => 
    {
        // Manually propagate correlation to new thread
        CorrelationContext.Current = correlationId;
        _logger.InfoWithCorrelation("Processing on background thread");
    });
}
```

#### Provider Request Tracking

```csharp
public async Task<List<ImportListItemInfo>> GetRecommendationsAsync(
    string prompt, 
    LibraryProfile profile)
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation($"Starting {_provider} recommendation request");
        
        try
        {
            var response = await SendRequestAsync(prompt);
            _logger.InfoWithCorrelation($"Received response in {response.Duration}ms");
            return response.Recommendations;
        }
        catch (Exception ex)
        {
            _logger.ErrorWithCorrelation(ex, "Provider request failed");
            throw;
        }
    }
}
```

#### Cache Correlation

```csharp
public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
{
    var correlationId = CorrelationContext.Current;
    _logger.DebugWithCorrelation($"Cache lookup for key: {key}");
    
    if (_cache.TryGetValue(key, out T cached))
    {
        _logger.DebugWithCorrelation("Cache hit");
        return cached;
    }
    
    _logger.DebugWithCorrelation("Cache miss, executing factory");
    var value = await factory();
    
    _cache.Set(key, value);
    _logger.InfoWithCorrelation($"Cached result for correlation {correlationId}");
    
    return value;
}
```

## Correlation ID Format

Correlation IDs follow a predictable format for easy identification:

```
Format: yyyyMMddHHmmss_xxxxxxxx
Example: 20241219143052_a3f7b2c1

Components:
- Timestamp: 14-digit UTC timestamp
- Separator: Underscore character
- Random: 8-character hexadecimal string
```

This format provides:
- **Temporal Ordering**: Sort by correlation ID to see chronological order
- **Uniqueness**: 2^32 possible random values per second
- **Readability**: Human-readable timestamp component
- **Parsing**: Easy to extract timestamp for analysis

## Logging with Correlation

### Available Logging Methods

```csharp
// Basic logging with correlation
_logger.DebugWithCorrelation("Debug message");
_logger.InfoWithCorrelation("Info message");
_logger.WarnWithCorrelation("Warning message");
_logger.ErrorWithCorrelation("Error message");

// Formatted logging with parameters
_logger.InfoWithCorrelation("Processing {0} items", itemCount);
_logger.ErrorWithCorrelation("Failed after {0} attempts", retryCount);

// Exception logging with correlation
_logger.ErrorWithCorrelation(exception, "Operation failed");
```

### Log Output Format

```
[20241219143052_a3f7b2c1] Starting recommendation request
[20241219143052_a3f7b2c1] Analyzing library: 500 artists, 2000 albums
[20241219143052_a3f7b2c1] Provider OpenAI selected
[20241219143052_a3f7b2c1] Cache miss, fetching from provider
[20241219143052_a3f7b2c1] Response received in 1250ms
[20241219143052_a3f7b2c1] Completed successfully, 15 recommendations
```

## URL Sanitization

The `UrlSanitizer` utility ensures sensitive data isn't logged:

### Basic URL Sanitization

```csharp
var url = "https://api.openai.com/v1/chat?api_key=sk-12345&model=gpt-4";
var sanitized = UrlSanitizer.SanitizeUrl(url);
// Result: "https://api.openai.com/v1/chat"

var apiUrl = "https://api.service.com/endpoint?token=secret";
var sanitizedApi = UrlSanitizer.SanitizeApiUrl(apiUrl);
// Result: "https://api.service.com/endpoint"
```

### Logging with Sanitization

```csharp
public async Task<HttpResponse> SendRequestAsync(string url)
{
    var sanitizedUrl = UrlSanitizer.SanitizeApiUrl(url);
    _logger.InfoWithCorrelation($"Sending request to {sanitizedUrl}");
    
    try
    {
        return await _httpClient.GetAsync(url);
    }
    catch (Exception ex)
    {
        _logger.ErrorWithCorrelation(ex, $"Request failed: {sanitizedUrl}");
        throw;
    }
}
```

## Integration Points

### BrainarrOrchestrator Integration

```csharp
public async Task<List<ImportListItemInfo>> FetchAsync()
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation("Starting Brainarr import list fetch");
        
        var library = await _analyzer.AnalyzeAsync();
        var recommendations = await _aiService.GetRecommendationsAsync(library);
        
        _logger.InfoWithCorrelation($"Fetch completed: {recommendations.Count} items");
        return recommendations;
    }
}
```

### Provider Factory Integration

```csharp
public IAIProvider CreateProvider(AIProvider provider)
{
    _logger.DebugWithCorrelation($"Creating provider instance: {provider}");
    
    var instance = provider switch
    {
        AIProvider.OpenAI => new OpenAIProvider(_settings, _httpClient),
        AIProvider.Claude => new ClaudeProvider(_settings, _httpClient),
        _ => throw new NotSupportedException($"Provider {provider} not supported")
    };
    
    _logger.InfoWithCorrelation($"Provider {provider} created successfully");
    return instance;
}
```

## Performance Considerations

### Thread-Local Storage

CorrelationContext uses `ThreadLocal<string>` for storage:

**Advantages**:
- No lock contention between threads
- Fast access (O(1) lookup)
- Automatic cleanup when thread ends

**Considerations**:
- Each thread has its own correlation context
- Async operations may switch threads (use explicit propagation)
- ThreadLocal has small memory overhead per thread

### Memory Management

```csharp
// Clearing correlation context when done
public void ProcessBatch()
{
    try
    {
        CorrelationContext.StartNew();
        // Process batch...
    }
    finally
    {
        CorrelationContext.Clear(); // Explicit cleanup
    }
}
```

### Best Practices for Performance

1. **Use Scopes**: Automatic cleanup with `using` statements
2. **Avoid Excessive Nesting**: Deep scopes add overhead
3. **Clear When Done**: Explicit cleanup for long-running operations
4. **Batch Operations**: Share correlation across related operations

## Testing with Correlation Context

### Unit Testing

```csharp
[Fact]
public void Should_Generate_Unique_CorrelationIds()
{
    var id1 = CorrelationContext.GenerateCorrelationId();
    var id2 = CorrelationContext.GenerateCorrelationId();
    
    Assert.NotEqual(id1, id2);
    Assert.Matches(@"^\d{14}_[a-f0-9]{8}$", id1);
}

[Fact]
public void Should_Restore_Previous_Context_After_Scope()
{
    CorrelationContext.Current = "original";
    
    using (var scope = new CorrelationScope("temporary"))
    {
        Assert.Equal("temporary", CorrelationContext.Current);
    }
    
    Assert.Equal("original", CorrelationContext.Current);
}
```

### Integration Testing

```csharp
[Fact]
public async Task Should_Track_Correlation_Through_Request_Chain()
{
    var correlationId = CorrelationContext.StartNew();
    var logs = new List<string>();
    
    // Configure test logger to capture correlation
    var logger = new TestLogger(message => logs.Add(message));
    
    // Execute operation
    await orchestrator.FetchAsync();
    
    // Verify all logs have same correlation
    Assert.All(logs, log => Assert.Contains(correlationId, log));
}
```

## Troubleshooting

### Common Issues

#### Correlation Lost After Async Call

**Problem**: Correlation ID missing after `await`

**Solution**: Explicitly propagate correlation
```csharp
var correlationId = CorrelationContext.Current;
await SomeAsyncOperation();
CorrelationContext.Current = correlationId; // Restore if needed
```

#### Different Correlation IDs in Same Request

**Problem**: Multiple correlation IDs appearing in single logical operation

**Solution**: Use scopes consistently
```csharp
// Wrong: Creates new correlation on each call
_logger.InfoWithCorrelation("Step 1"); // New ID
_logger.InfoWithCorrelation("Step 2"); // Different new ID

// Correct: Share correlation across operation
using (var scope = new CorrelationScope())
{
    _logger.InfoWithCorrelation("Step 1"); // Same ID
    _logger.InfoWithCorrelation("Step 2"); // Same ID
}
```

#### Memory Leak in Long-Running Process

**Problem**: ThreadLocal storage not cleared

**Solution**: Explicit cleanup
```csharp
public void LongRunningProcess()
{
    while (running)
    {
        using (var scope = new CorrelationScope())
        {
            ProcessItem();
        } // Automatic cleanup via Dispose
        
        // Or explicit clear
        CorrelationContext.Clear();
    }
}
```

## Security Considerations

### Sensitive Data Protection

1. **Never Log API Keys**: Use `UrlSanitizer` for all external URLs
2. **Mask Personal Data**: Don't include user data in correlation logs
3. **Sanitize Error Messages**: Clean exception details before logging

```csharp
// Bad: Logs sensitive data
_logger.ErrorWithCorrelation($"API call failed: {fullUrl}");

// Good: Sanitizes URL
var sanitized = UrlSanitizer.SanitizeApiUrl(fullUrl);
_logger.ErrorWithCorrelation($"API call failed: {sanitized}");
```

### Correlation ID Security

- Correlation IDs are not secrets (safe to log/display)
- Don't use correlation IDs for authentication
- Correlation IDs should not contain user information
- Safe to include in error messages to users

## Example: Complete Request Flow

Here's how correlation context flows through a complete recommendation request:

```csharp
// 1. Entry point - BrainarrImportList.cs
public override async Task<List<ImportListItemInfo>> FetchAsync()
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation("Starting import list fetch");
        
        // 2. Orchestrator - BrainarrOrchestrator.cs
        var items = await _orchestrator.FetchRecommendationsAsync();
        
        _logger.InfoWithCorrelation($"Completed: {items.Count} items");
        return items;
    }
}

// 3. Inside Orchestrator
public async Task<List<ImportListItemInfo>> FetchRecommendationsAsync()
{
    _logger.DebugWithCorrelation("Checking cache");
    
    // 4. Cache check - RecommendationCache.cs
    if (await _cache.TryGetAsync(key))
    {
        _logger.InfoWithCorrelation("Cache hit");
        return cached;
    }
    
    // 5. Library analysis - LibraryAnalyzer.cs
    _logger.InfoWithCorrelation("Analyzing library");
    var profile = await _analyzer.AnalyzeAsync();
    
    // 6. AI Service - AIService.cs  
    _logger.InfoWithCorrelation("Getting AI recommendations");
    var recommendations = await _aiService.GetRecommendationsAsync(profile);
    
    // 7. Provider call - OpenAIProvider.cs
    _logger.InfoWithCorrelation($"Calling OpenAI API");
    var response = await _provider.GetCompletionAsync(prompt);
    
    // 8. Cache update
    _logger.InfoWithCorrelation("Updating cache");
    await _cache.SetAsync(key, recommendations);
    
    return recommendations;
}

// Log output (all with same correlation ID):
// [20241219143052_a3f7b2c1] Starting import list fetch
// [20241219143052_a3f7b2c1] Checking cache
// [20241219143052_a3f7b2c1] Cache miss
// [20241219143052_a3f7b2c1] Analyzing library
// [20241219143052_a3f7b2c1] Getting AI recommendations
// [20241219143052_a3f7b2c1] Calling OpenAI API
// [20241219143052_a3f7b2c1] Updating cache
// [20241219143052_a3f7b2c1] Completed: 15 items
```

## Future Enhancements

### Planned Features

1. **OpenTelemetry Integration**: Export correlation to distributed tracing systems
2. **Metrics Collection**: Aggregate performance metrics by correlation
3. **Async Local Storage**: Better async/await support with AsyncLocal<T>
4. **Correlation Propagation**: HTTP header propagation for external services
5. **Structured Logging**: Integration with structured logging frameworks

### Extension Points

The correlation context system is designed for extensibility:

```csharp
// Custom correlation generator
public class CustomCorrelationContext : CorrelationContext
{
    public static new string GenerateCorrelationId()
    {
        // Custom format: SERVICE_TIMESTAMP_RANDOM
        return $"BRAINARR_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
    }
}

// Custom scope with metadata
public class MetadataCorrelationScope : CorrelationScope
{
    public Dictionary<string, object> Metadata { get; }
    
    public MetadataCorrelationScope(string correlationId, Dictionary<string, object> metadata)
        : base(correlationId)
    {
        Metadata = metadata;
    }
}
```

## Related Documentation

- [Performance Tuning Guide](PERFORMANCE_TUNING.md) - Optimize correlation overhead
- [Troubleshooting Guide](TROUBLESHOOTING.md) - Debug correlation issues  
- [API Reference](API_REFERENCE.md) - Complete API documentation
- [Testing Guide](TESTING_GUIDE.md) - Testing best practices