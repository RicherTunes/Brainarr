# Brainarr API Reference

## Table of Contents
- [Core Interfaces](#core-interfaces)
- [Service Classes](#service-classes)
- [Configuration Classes](#configuration-classes)
- [Provider Implementations](#provider-implementations)
- [Supporting Classes](#supporting-classes)
- [Extension Points](#extension-points)

## Core Interfaces

### IAIProvider
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

The base interface that all AI providers must implement.

```csharp
public interface IAIProvider
{
    Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
    Task<bool> TestConnectionAsync();
    string ProviderName { get; }
}
```

**Methods:**
- `GetRecommendationsAsync(string prompt)` - Gets music recommendations from the AI provider
- `TestConnectionAsync()` - Tests connectivity to the provider
- `ProviderName` - Display name of the provider

### IAIService
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Main orchestrator for managing multiple AI providers with failover.

```csharp
public interface IAIService
{
    Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
    Task<Dictionary<string, bool>> TestAllProvidersAsync();
    Task<Dictionary<string, ProviderHealthInfo>> GetProviderHealthAsync();
    void RegisterProvider(IAIProvider provider, int priority = 100);
    void UpdateConfiguration(BrainarrSettings settings);
    AIServiceMetrics GetMetrics();
}
```

**Key Features:**
- Automatic failover between providers
- Health monitoring and metrics
- Provider registration with priority

### ILibraryAnalyzer
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Analyzes music library to create profiles for recommendations.

```csharp
public interface ILibraryAnalyzer
{
    LibraryProfile AnalyzeLibrary(List<Artist> artists, List<Album> albums);
    Task<LibraryProfile> AnalyzeLibraryAsync(List<Artist> artists, List<Album> albums);
    LibraryStatistics GetStatistics(LibraryProfile profile);
}
```

### IProviderFactory
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Factory for creating provider instances.

```csharp
public interface IProviderFactory
{
    IAIProvider CreateProvider(BrainarrSettings settings);
    List<string> GetAvailableProviders();
    bool IsProviderSupported(AIProvider provider);
}
```

## Service Classes

### AIService
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Main service orchestrating AI providers with automatic failover.

```csharp
public class AIService : IAIService
{
    public AIService(
        Logger logger,
        IProviderHealthMonitor healthMonitor,
        IRetryPolicy retryPolicy,
        IRateLimiter rateLimiter,
        IRecommendationSanitizer sanitizer);
}
```

**Features:**
- Chain of responsibility pattern for failover
- Rate limiting per provider
- Health monitoring with circuit breaker
- Retry policies with exponential backoff
- Response sanitization

### LibraryAnalyzer
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services.Core`

Analyzes music library and creates optimized profiles.

```csharp
public class LibraryAnalyzer : ILibraryAnalyzer
{
    public LibraryProfile AnalyzeLibrary(List<Artist> artists, List<Album> albums);
    public Dictionary<string, int> CalculateGenreDistribution(List<Artist> artists);
    public List<string> ExtractTopArtists(List<Artist> artists, int count = 20);
}
```

**Key Methods:**
- `AnalyzeLibrary` - Creates comprehensive library profile
- `CalculateGenreDistribution` - Analyzes genre preferences
- `ExtractTopArtists` - Identifies most significant artists

### LibraryAwarePromptBuilder
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Builds optimized prompts based on library analysis and token constraints.

```csharp
public class LibraryAwarePromptBuilder
{
    public string BuildLibraryAwarePrompt(
        LibraryProfile profile,
        List<Artist> allArtists,
        List<Album> allAlbums,
        BrainarrSettings settings);
}
```

**Token Optimization:**
- Minimal: 2000 tokens (local models)
- Balanced: 3000 tokens (standard)
- Comprehensive: 4000 tokens (premium)

### IterativeRecommendationStrategy
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Implements intelligent iteration to minimize duplicates.

```csharp
public class IterativeRecommendationStrategy
{
    public Task<List<Recommendation>> GetIterativeRecommendationsAsync(
        IAIProvider provider,
        LibraryProfile profile,
        List<Artist> allArtists,
        List<Album> allAlbums,
        BrainarrSettings settings);
}
```

**Algorithm:**
- Maximum 3 iterations
- Adaptive request sizing (1.5x, 2x, 3x multipliers)
- Duplicate tracking and feedback
- 70% minimum success rate threshold

### ModelDetectionService
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Auto-detects available models for local providers.

```csharp
public class ModelDetectionService : IModelDetectionService
{
    public Task<List<string>> DetectOllamaModelsAsync(string baseUrl);
    public Task<List<string>> DetectLMStudioModelsAsync(string baseUrl);
    public Task<bool> ValidateModelAsync(string baseUrl, string model, AIProvider provider);
}
```

### ProviderHealthMonitor
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Monitors provider health and availability.

```csharp
public class ProviderHealthMonitor : IProviderHealthMonitor
{
    public Task<HealthStatus> CheckHealthAsync(string provider, string url);
    public void RecordSuccess(string provider, long responseTime);
    public void RecordFailure(string provider, string error);
    public ProviderHealthInfo GetHealthInfo(string provider);
}
```

**Health States:**
- `Healthy` - Provider operational
- `Degraded` - Intermittent issues
- `Unhealthy` - Provider unavailable

### RateLimiter
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Implements per-provider rate limiting.

```csharp
public class RateLimiter : IRateLimiter
{
    public Task<T> ExecuteAsync<T>(string key, Func<Task<T>> operation);
    public void Configure(string key, int requestsPerMinute, int burstSize);
    public bool IsThrottled(string key);
}
```

**Default Limits:**
- 10 requests per minute
- Burst size of 5
- Configurable per provider

### RecommendationCache
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Caches recommendations to reduce API calls.

```csharp
public class RecommendationCache : IRecommendationCache
{
    public bool TryGet(string key, out List<Recommendation> recommendations);
    public void Set(string key, List<Recommendation> recommendations, TimeSpan? ttl = null);
    public string GenerateCacheKey(string provider, int count, string libraryFingerprint);
    public void Clear();
}
```

**Cache Configuration:**
- Default TTL: 60 minutes
- Maximum entries: 100
- LRU eviction policy

## Configuration Classes

### BrainarrSettings
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr`

Main configuration class for the plugin.

```csharp
public class BrainarrSettings : ImportListSettingsBase<BrainarrSettings>
{
    // Provider selection
    public AIProvider Provider { get; set; }
    
    // Provider-specific settings
    public string BaseUrl { get; set; }
    public string ApiKey { get; set; }
    public string SelectedModel { get; set; }
    
    // Discovery settings
    public DiscoveryMode DiscoveryMode { get; set; }
    public int MaxRecommendations { get; set; }
    public SamplingStrategy SamplingStrategy { get; set; }
    
    // Cache settings
    public int CacheDurationMinutes { get; set; }
}
```

### ProviderConfiguration
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Configuration`

Base configuration for providers.

```csharp
public abstract class ProviderConfiguration
{
    public abstract bool IsValid();
    public abstract string GetConnectionString();
    public virtual int GetTimeout() => 30;
}
```

### Constants
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Configuration`

Central configuration constants.

```csharp
public static class BrainarrConstants
{
    // URLs
    public const string DefaultOllamaUrl = "http://localhost:11434";
    public const string DefaultLMStudioUrl = "http://localhost:1234";
    
    // Limits
    public const int MinRecommendations = 1;
    public const int MaxRecommendations = 50;
    public const int DefaultRecommendations = 20;
    
    // Timeouts
    public const int DefaultAITimeout = 30;
    public const int MaxAITimeout = 120;
    
    // Rate Limiting
    public const int RequestsPerMinute = 10;
    public const int BurstSize = 5;
}
```

## Provider Implementations

### OllamaProvider
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Local Ollama provider implementation.

```csharp
public class OllamaProvider : IAIProvider
{
    public OllamaProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger);
    
    // Endpoints
    // POST /api/generate - Generate text
    // GET /api/tags - List models
}
```

### LMStudioProvider
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Local LM Studio provider implementation.

```csharp
public class LMStudioProvider : IAIProvider
{
    public LMStudioProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger);
    
    // OpenAI-compatible endpoints
    // POST /v1/chat/completions
    // GET /v1/models
}
```

### OpenAIProvider
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services.Providers`

OpenAI GPT models provider.

```csharp
public class OpenAIProvider : IAIProvider
{
    public OpenAIProvider(string apiKey, string model, IHttpClient httpClient, Logger logger);
    
    // Models: gpt-4o, gpt-4o-mini, gpt-3.5-turbo
}
```

### AnthropicProvider
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services.Providers`

Anthropic Claude models provider.

```csharp
public class AnthropicProvider : IAIProvider
{
    public AnthropicProvider(string apiKey, string model, IHttpClient httpClient, Logger logger);
    
    // Models: claude-3-5-sonnet, claude-3-5-haiku, claude-3-opus
}
```

### Additional Providers
- **DeepSeekProvider** - Ultra cost-effective Chinese models
- **GeminiProvider** - Google Gemini with free tier
- **GroqProvider** - Ultra-fast inference
- **PerplexityProvider** - Web-enhanced responses
- **OpenRouterProvider** - Gateway to 200+ models

## Supporting Classes

### Recommendation
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Represents a music recommendation.

```csharp
public class Recommendation
{
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Genre { get; set; }
    public double Confidence { get; set; }  // 0.0 to 1.0
    public string Reason { get; set; }
    public int? ReleaseYear { get; set; }
}
```

### LibraryProfile
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Profile of user's music library.

```csharp
public class LibraryProfile
{
    public int TotalArtists { get; set; }
    public int TotalAlbums { get; set; }
    public Dictionary<string, int> TopGenres { get; set; }
    public List<string> TopArtists { get; set; }
    public List<string> RecentAdditions { get; set; }
    public double GenreDiversity { get; set; }
}
```

### ProviderHealthInfo
**Namespace:** `NzbDrone.Core.ImportLists.Brainarr.Services`

Health information for a provider.

```csharp
public class ProviderHealthInfo
{
    public string ProviderName { get; set; }
    public HealthStatus Status { get; set; }
    public double SuccessRate { get; set; }
    public double AverageResponseTime { get; set; }
    public int TotalRequests { get; set; }
    public int FailedRequests { get; set; }
    public string LastError { get; set; }
    public bool IsAvailable { get; set; }
}
```

## Extension Points

### Adding a New Provider

1. **Implement IAIProvider interface:**
```csharp
public class CustomProvider : IAIProvider
{
    public string ProviderName => "Custom Provider";
    
    public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
    {
        // Implementation
    }
    
    public async Task<bool> TestConnectionAsync()
    {
        // Test connectivity
    }
}
```

2. **Register in ProviderRegistry:**
```csharp
registry.Register(AIProvider.Custom, () => new CustomProvider(...));
```

3. **Add to BrainarrSettings enum:**
```csharp
public enum AIProvider
{
    // ... existing providers
    Custom = 10
}
```

4. **Add configuration UI fields in BrainarrSettings**

### Custom Recommendation Strategies

Implement custom recommendation strategies by extending the base classes:

```csharp
public class CustomRecommendationStrategy
{
    public Task<List<Recommendation>> GetRecommendationsAsync(
        IAIProvider provider,
        LibraryProfile profile,
        BrainarrSettings settings)
    {
        // Custom logic
    }
}
```

### Custom Sanitizers

Implement custom sanitization logic:

```csharp
public class CustomSanitizer : IRecommendationSanitizer
{
    public List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations)
    {
        // Custom sanitization
    }
}
```

## Error Handling

### Exception Types

- `ProviderException` - Provider-specific errors
- `RateLimitException` - Rate limit exceeded
- `ConfigurationException` - Invalid configuration
- `TimeoutException` - Request timeout
- `CircuitOpenException` - Circuit breaker open

### Error Recovery

The plugin implements multiple layers of error recovery:

1. **Retry Policy** - Exponential backoff with jitter
2. **Circuit Breaker** - Prevents cascading failures
3. **Provider Failover** - Automatic fallback to secondary providers
4. **Cache Fallback** - Returns cached results on total failure

## Performance Considerations

### Token Optimization
- Minimal sampling for local models (2K tokens)
- Balanced sampling for standard providers (3K tokens)
- Comprehensive sampling for premium providers (4K tokens)

### Caching Strategy
- Memory cache with 60-minute TTL
- Cache key includes library fingerprint
- LRU eviction on size limits

### Rate Limiting
- Per-provider rate limits
- Token bucket algorithm
- Configurable burst capacity

## Testing

### Unit Testing
```csharp
[Test]
public void TestProviderConnection()
{
    var provider = new OllamaProvider(url, model, httpClient, logger);
    var result = await provider.TestConnectionAsync();
    Assert.IsTrue(result);
}
```

### Integration Testing
```csharp
[TestCategory("Integration")]
public async Task TestEndToEndRecommendations()
{
    var service = new AIService(...);
    var recommendations = await service.GetRecommendationsAsync(prompt);
    Assert.That(recommendations.Count, Is.GreaterThan(0));
}
```

---

*API Version: 1.0.0 | Last Updated: January 2025*