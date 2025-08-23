# Correlation Context & Request Tracking Documentation

## Overview

Brainarr implements a comprehensive correlation tracking system that provides end-to-end request tracing across all components. This feature enables better debugging, performance monitoring, and operational visibility.

## Key Components

### CorrelationContext Class

The `CorrelationContext` class provides thread-safe correlation ID management:

```csharp
// Automatic correlation ID generation
var correlationId = CorrelationContext.Current;

// Manual correlation ID creation
var newId = CorrelationContext.StartNew();

// Clear context when done
CorrelationContext.Clear();
```

### Correlation ID Format

Correlation IDs follow a human-readable format:
```
yyyyMMddHHmmss_[8-char-hex]
Example: 20241219143052_a3f7b2c1
```

This format provides:
- **Timestamp**: When the request started
- **Unique Identifier**: 8-character hex string from GUID
- **Sortability**: IDs naturally sort by time
- **Readability**: Easy to identify in logs

## Usage Patterns

### Basic Usage

The correlation system automatically manages IDs across request boundaries:

```csharp
// Correlation ID is automatically created on first access
_logger.InfoWithCorrelation("Starting recommendation fetch");

// Same ID is maintained throughout the request
_logger.DebugWithCorrelation($"Using provider: {provider}");

// Correlation ID flows through all service calls
var recommendations = await aiService.GetRecommendations();
```

### Scoped Correlation

Use `CorrelationScope` for explicit correlation boundaries:

```csharp
using (var scope = new CorrelationScope())
{
    // All operations within scope share same correlation ID
    _logger.InfoWithCorrelation($"Processing batch {batchId}");
    
    await ProcessBatch();
    
    _logger.InfoWithCorrelation("Batch complete");
} // Previous correlation ID restored
```

### Manual Correlation Control

For advanced scenarios requiring manual control:

```csharp
// Save current context
var previousId = CorrelationContext.Current;

// Set specific correlation ID
CorrelationContext.Current = "custom_correlation_123";

// Perform operations
await PerformOperations();

// Restore previous context
CorrelationContext.Current = previousId;
```

## Logging Integration

### Logger Extension Methods

The system provides correlation-aware logging methods:

```csharp
// Basic logging with correlation
_logger.DebugWithCorrelation("Debug message");
_logger.InfoWithCorrelation("Info message");
_logger.WarnWithCorrelation("Warning message");
_logger.ErrorWithCorrelation("Error message");

// Parameterized logging
_logger.InfoWithCorrelation("Processing {0} items", itemCount);

// Exception logging
_logger.ErrorWithCorrelation(ex, "Operation failed");
```

### Log Output Format

Logs include correlation ID in brackets:

```
[20241219143052_a3f7b2c1] Starting recommendation fetch
[20241219143052_a3f7b2c1] Analyzing library: 245 artists, 1234 albums
[20241219143052_a3f7b2c1] Generated recommendations: 15 items
```

## Security Features

### URL Sanitization

The system includes URL sanitization to prevent sensitive data leakage:

```csharp
// Sanitize URLs before logging
var safeUrl = UrlSanitizer.SanitizeUrl(apiUrl);
_logger.DebugWithCorrelation($"Calling API: {safeUrl}");

// Specialized API URL sanitization
var safeApiUrl = UrlSanitizer.SanitizeApiUrl(endpoint);
```

Sanitization removes:
- Query parameters (may contain API keys)
- URL fragments
- Authentication tokens
- Sensitive path components

**Before**: `https://api.service.com/v1/data?api_key=secret123&user=admin#section`
**After**: `https://api.service.com/v1/data`

## Implementation Areas

### 1. Main Entry Point (BrainarrImportList)

```csharp
public override async Task<IList<ImportListItemInfo>> Fetch()
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation("Starting Brainarr import list fetch");
        // All subsequent operations share this correlation ID
    }
}
```

### 2. Service Layer (AIService)

```csharp
public async Task<List<ImportListItemInfo>> GetRecommendationsAsync()
{
    _logger.DebugWithCorrelation($"Provider: {provider}, Model: {model}");
    // Correlation ID flows through all service calls
}
```

### 3. Provider Implementations

```csharp
public async Task<List<ImportListItemInfo>> GetRecommendationsAsync()
{
    _logger.InfoWithCorrelation($"Ollama request to {_baseUrl}");
    // Provider-specific operations tracked with same ID
}
```

### 4. Error Handling

```csharp
catch (Exception ex)
{
    _logger.ErrorWithCorrelation(ex, "Provider failed");
    // Correlation ID helps trace error origin
}
```

## Benefits

### 1. Enhanced Debugging
- **Request Tracing**: Follow requests through entire system
- **Error Correlation**: Link errors to specific requests
- **Performance Analysis**: Identify bottlenecks in request flow

### 2. Operational Visibility
- **Request Metrics**: Track request patterns and volumes
- **Service Dependencies**: Understand service interaction patterns
- **Failure Analysis**: Correlate failures across components

### 3. Support & Troubleshooting
- **User Issue Resolution**: Trace specific user requests
- **Log Aggregation**: Group related log entries
- **Incident Response**: Quickly identify affected components

## Best Practices

### DO:
- ✅ Use correlation-aware logging methods consistently
- ✅ Create correlation scopes for batch operations
- ✅ Sanitize URLs before logging
- ✅ Include correlation ID in error messages
- ✅ Preserve correlation context across async boundaries

### DON'T:
- ❌ Log sensitive data even with correlation
- ❌ Create nested correlation scopes unnecessarily
- ❌ Modify correlation ID mid-request
- ❌ Forget to clear correlation context in long-running processes

## Monitoring Integration

### Log Aggregation

Correlation IDs enable powerful log queries:

```sql
-- Find all logs for a specific request
SELECT * FROM logs 
WHERE message LIKE '%[20241219143052_a3f7b2c1]%'
ORDER BY timestamp;

-- Find failed requests
SELECT correlation_id, COUNT(*) as error_count
FROM logs
WHERE level = 'ERROR'
GROUP BY correlation_id
HAVING error_count > 0;
```

### Metrics Collection

Track request metrics by correlation:
- Request duration
- Service call count
- Error rate per request
- Provider usage patterns

## Thread Safety

The correlation system is thread-safe:
- Uses `ThreadLocal<string>` for isolation
- Each thread maintains independent context
- Safe for concurrent request processing
- No cross-thread contamination

## Performance Considerations

The correlation system has minimal overhead:
- **Memory**: ~100 bytes per active request
- **CPU**: Negligible (string concatenation only)
- **Thread Local Storage**: Automatic cleanup
- **No External Dependencies**: Pure .NET implementation

## Future Enhancements

Planned improvements for correlation tracking:
- **Distributed Tracing**: OpenTelemetry integration
- **Request Sampling**: Configurable detail levels
- **Correlation Forwarding**: Pass to external services
- **Metrics Dashboard**: Correlation-based analytics

## Troubleshooting

### Missing Correlation IDs

**Problem**: Logs show empty correlation IDs
**Solution**: Ensure using correlation-aware logging methods

### Correlation ID Changes Mid-Request

**Problem**: Different IDs for same logical request
**Solution**: Check for correlation context clearing or scope disposal

### Thread Context Loss

**Problem**: Correlation lost after async operations
**Solution**: Correlation context is thread-local; preserved automatically in async/await

## Related Documentation

- [Troubleshooting Guide](TROUBLESHOOTING.md) - Using correlation for debugging
- [Debugging Guide](TROUBLESHOOTING.md) - Using correlation for debugging
- [Architecture Overview](ARCHITECTURE.md) - System design and monitoring