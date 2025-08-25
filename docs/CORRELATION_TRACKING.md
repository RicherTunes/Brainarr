# Correlation Context and Request Tracking

## Overview

Brainarr implements comprehensive correlation tracking to trace requests throughout the entire recommendation pipeline. This feature enables better debugging, monitoring, and performance analysis by providing a unique identifier that follows each request across all components.

## What is Correlation Tracking?

Correlation tracking assigns a unique ID to each recommendation request, which is then passed through all layers of the application. This allows you to:

- Track a single request across multiple log files
- Identify performance bottlenecks in specific requests
- Debug issues by filtering logs for a specific correlation ID
- Monitor request flow through different providers and services
- Correlate errors with specific user actions

## Implementation Details

### CorrelationContext Class

The `CorrelationContext` class manages correlation IDs using thread-local storage:

```csharp
public static class CorrelationContext
{
    // Gets or creates correlation ID for current thread
    public static string Current { get; set; }
    
    // Generates new correlation ID
    public static string StartNew()
    
    // Clears current correlation context
    public static void Clear()
}
```

### Correlation ID Format

Correlation IDs follow the format: `YYYYMMDDHHMMSS_XXXXXXXX`

Example: `20241219143052_a3f7b2c1`

- First part: Timestamp for chronological sorting
- Second part: Random hex string for uniqueness

### CorrelationScope

Use `CorrelationScope` for automatic context management:

```csharp
using (var scope = new CorrelationScope())
{
    // All operations within scope share same correlation ID
    var recommendations = await GetRecommendations();
    // Correlation ID automatically restored when scope ends
}
```

## Usage Examples

### Basic Usage

```csharp
// Start new correlation context
var correlationId = CorrelationContext.StartNew();
_logger.InfoWithCorrelation($"Starting recommendation request");

// Access current correlation ID
var currentId = CorrelationContext.Current;

// Clear correlation context
CorrelationContext.Clear();
```

### Logger Extensions

Brainarr provides logger extensions that automatically include correlation IDs:

```csharp
// Debug logging with correlation
_logger.DebugWithCorrelation("Fetching recommendations from provider");

// Info logging with correlation
_logger.InfoWithCorrelation($"Received {count} recommendations");

// Warning with correlation
_logger.WarnWithCorrelation("Provider response slower than expected");

// Error with correlation and exception
_logger.ErrorWithCorrelation(ex, "Failed to parse provider response");
```

### Scoped Operations

```csharp
public async Task<List<Recommendation>> ProcessRequest()
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation($"Processing request");
        
        try
        {
            var result = await FetchFromProvider();
            _logger.InfoWithCorrelation($"Request successful");
            return result;
        }
        catch (Exception ex)
        {
            _logger.ErrorWithCorrelation(ex, "Request failed");
            throw;
        }
    }
    // Previous correlation ID restored here
}
```

## Log Output Examples

### Standard Logs with Correlation

```log
2024-12-19 14:30:52.123 [20241219143052_a3f7b2c1] INFO: Starting recommendation cycle
2024-12-19 14:30:52.456 [20241219143052_a3f7b2c1] DEBUG: Library analysis: 500 artists, 2000 albums
2024-12-19 14:30:52.789 [20241219143052_a3f7b2c1] DEBUG: Selected provider: OpenAI
2024-12-19 14:30:53.123 [20241219143052_a3f7b2c1] DEBUG: Sending request to AI provider
2024-12-19 14:30:55.456 [20241219143052_a3f7b2c1] INFO: Received 20 recommendations
2024-12-19 14:30:55.789 [20241219143052_a3f7b2c1] DEBUG: Filtering duplicates: 3 removed
2024-12-19 14:30:56.123 [20241219143052_a3f7b2c1] INFO: Final recommendations: 17
```

### Error Tracking with Correlation

```log
2024-12-19 14:35:10.123 [20241219143510_b4c8d2e3] ERROR: Provider timeout after 30s
2024-12-19 14:35:10.456 [20241219143510_b4c8d2e3] INFO: Attempting failover to Anthropic
2024-12-19 14:35:10.789 [20241219143510_b4c8d2e3] WARN: Circuit breaker opened for OpenAI
2024-12-19 14:35:12.123 [20241219143510_b4c8d2e3] INFO: Failover successful
```

## Debugging with Correlation IDs

### Finding All Logs for a Request

```bash
# Search for specific correlation ID in logs
grep "20241219143052_a3f7b2c1" /var/log/lidarr/lidarr.txt

# Follow a request in real-time
tail -f /var/log/lidarr/lidarr.txt | grep "20241219143052_a3f7b2c1"
```

### Performance Analysis

```bash
# Extract timing for specific request
grep "20241219143052_a3f7b2c1" lidarr.txt | grep -E "(Starting|Completed|Duration)"
```

### Error Investigation

```bash
# Find all errors for a correlation ID
grep "20241219143052_a3f7b2c1" lidarr.txt | grep -E "(ERROR|WARN|FAIL)"
```

## URL Sanitization

The correlation tracking includes URL sanitization to prevent logging sensitive data:

```csharp
public static class UrlSanitizer
{
    // Removes query parameters and sensitive data
    public static string SanitizeUrl(string url)
    
    // Special handling for API endpoints
    public static string SanitizeApiUrl(string url)
}
```

### Sanitization Examples

```csharp
// Original URL
"https://api.openai.com/v1/chat?api_key=sk-abc123&model=gpt-4"

// Sanitized URL
"https://api.openai.com/v1/chat"

// API URL with masked parameters
"https://api.provider.com/endpoint?api_key=***&token=***"
```

## Integration Points

### 1. Import List Entry

Correlation starts when Lidarr triggers import list sync:

```csharp
public override IList<ImportListItemInfo> Fetch()
{
    using (var scope = new CorrelationScope())
    {
        _logger.InfoWithCorrelation("Starting Brainarr import sync");
        // Process recommendations
    }
}
```

### 2. Provider Selection

Correlation follows through provider selection:

```csharp
private IAIProvider SelectProvider()
{
    _logger.DebugWithCorrelation($"Evaluating {providers.Count} providers");
    // Provider selection logic
}
```

### 3. AI Request Processing

Each AI request maintains correlation:

```csharp
public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
{
    _logger.DebugWithCorrelation($"Sending request to {ProviderName}");
    // API call with correlation header if supported
}
```

### 4. Response Processing

Correlation continues through response handling:

```csharp
private List<ImportListItemInfo> ProcessRecommendations(List<Recommendation> recs)
{
    _logger.InfoWithCorrelation($"Processing {recs.Count} recommendations");
    // Validation, deduplication, conversion
}
```

## Monitoring and Metrics

### Correlation Metrics

Track correlation-based metrics:

```csharp
public class CorrelationMetrics
{
    public string CorrelationId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int ProviderAttempts { get; set; }
    public bool Success { get; set; }
    public int RecommendationCount { get; set; }
}
```

### Performance Tracking

```log
[20241219143052_a3f7b2c1] Performance Summary:
- Total Duration: 4.5s
- Library Analysis: 0.3s
- Provider Request: 3.2s
- Response Processing: 0.8s
- Deduplication: 0.2s
```

## Best Practices

### 1. Always Use Correlation in Critical Paths

```csharp
// Good
_logger.InfoWithCorrelation("Starting critical operation");

// Avoid
_logger.Info("Starting critical operation"); // No correlation
```

### 2. Preserve Correlation Across Async Boundaries

```csharp
public async Task ProcessAsync()
{
    var correlationId = CorrelationContext.Current;
    
    await Task.Run(() =>
    {
        // Restore correlation in new thread
        CorrelationContext.Current = correlationId;
        ProcessWork();
    });
}
```

### 3. Include Correlation in Error Reports

```csharp
catch (Exception ex)
{
    var errorReport = new ErrorReport
    {
        CorrelationId = CorrelationContext.Current,
        Exception = ex,
        Timestamp = DateTime.UtcNow
    };
    
    await SendErrorReport(errorReport);
    throw;
}
```

### 4. Use Scopes for Automatic Cleanup

```csharp
// Preferred - automatic cleanup
using (var scope = new CorrelationScope())
{
    await ProcessRequest();
}

// Avoid - manual management
CorrelationContext.StartNew();
await ProcessRequest();
CorrelationContext.Clear(); // Easy to forget
```

## Troubleshooting

### Missing Correlation IDs

**Issue**: Logs show empty or missing correlation IDs

**Solution**: Ensure CorrelationContext is initialized:
```csharp
if (string.IsNullOrEmpty(CorrelationContext.Current))
{
    CorrelationContext.StartNew();
}
```

### Correlation ID Not Propagating

**Issue**: Correlation ID lost in async operations

**Solution**: Capture and restore correlation:
```csharp
var correlationId = CorrelationContext.Current;
await SomeAsyncOperation().ConfigureAwait(false);
CorrelationContext.Current = correlationId;
```

### Performance Impact

**Issue**: Correlation tracking affecting performance

**Solution**: Use conditional compilation:
```csharp
#if DEBUG
    _logger.DebugWithCorrelation("Detailed debug info");
#endif
```

## Configuration

### Enable/Disable Correlation

In Brainarr settings:

```csharp
public class BrainarrSettings
{
    [FieldDefinition(Label = "Enable Correlation Tracking")]
    public bool EnableCorrelation { get; set; } = true;
}
```

### Log Level Configuration

Control correlation logging verbosity:

```xml
<logger name="NzbDrone.Core.ImportLists.Brainarr.*" minlevel="Debug">
  <filters>
    <when condition="contains('${message}','[${correlation}]')" action="Log" />
  </filters>
</logger>
```

## Future Enhancements

Planned improvements for correlation tracking:

1. **Distributed Tracing**: OpenTelemetry integration
2. **Correlation Headers**: Pass correlation through HTTP headers
3. **Visualization**: Request flow diagrams
4. **Analytics**: Correlation-based performance analytics
5. **Alerting**: Automatic alerts for stuck correlations

---

*Last Updated: 2025-08-25*
*Feature Version: 1.0.3*