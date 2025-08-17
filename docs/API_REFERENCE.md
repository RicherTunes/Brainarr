# Brainarr API Reference

## Core Interfaces

### IAIProvider

The primary interface that all AI providers must implement.

```csharp
public interface IAIProvider
{
    /// <summary>
    /// Gets the display name of the provider
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Tests connection to the AI provider
    /// </summary>
    /// <returns>True if connection successful</returns>
    Task<bool> TestConnectionAsync();
    
    /// <summary>
    /// Gets music recommendations from the AI provider
    /// </summary>
    /// <param name="prompt">Formatted prompt with library context</param>
    /// <returns>List of recommendations</returns>
    Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
}
```

### IProviderFactory

Factory interface for creating provider instances.

```csharp
public interface IProviderFactory
{
    /// <summary>
    /// Creates an AI provider based on settings
    /// </summary>
    /// <param name="settings">Configuration settings</param>
    /// <param name="httpClient">HTTP client for API calls</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Configured AI provider instance</returns>
    IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger);
    
    /// <summary>
    /// Checks if a provider is available and configured
    /// </summary>
    bool IsProviderAvailable(AIProvider providerType, BrainarrSettings settings);
}
```

### ILibraryAnalyzer

Analyzes user's music library for AI context.

```csharp
public interface ILibraryAnalyzer
{
    /// <summary>
    /// Analyzes library and creates profile
    /// </summary>
    /// <param name="artists">List of artists in library</param>
    /// <param name="albums">List of albums in library</param>
    /// <returns>Library profile with statistics</returns>
    LibraryProfile AnalyzeLibrary(List<Artist> artists, List<Album> albums);
    
    /// <summary>
    /// Calculates genre distribution
    /// </summary>
    Dictionary<string, double> CalculateGenreDistribution(List<Artist> artists);
    
    /// <summary>
    /// Identifies listening patterns
    /// </summary>
    ListeningPatterns IdentifyPatterns(List<Album> albums);
}
```

### IRecommendationCache

Caching interface for recommendations.

```csharp
public interface IRecommendationCache
{
    /// <summary>
    /// Generates cache key based on parameters
    /// </summary>
    string GenerateCacheKey(string provider, int maxRecommendations, string libraryFingerprint);
    
    /// <summary>
    /// Attempts to retrieve cached recommendations
    /// </summary>
    bool TryGet(string key, out List<ImportListItemInfo> recommendations);
    
    /// <summary>
    /// Stores recommendations in cache
    /// </summary>
    void Set(string key, List<ImportListItemInfo> recommendations, TimeSpan duration);
    
    /// <summary>
    /// Clears all cached recommendations
    /// </summary>
    void Clear();
}
```

## Provider Implementations

### Local Providers

#### OllamaProvider

```csharp
public class OllamaProvider : LocalAIProvider
{
    // Constructor
    public OllamaProvider(string baseUrl, string model, IHttpClient httpClient, Logger logger)
    
    // Methods
    public override async Task<bool> TestConnectionAsync()
    public override async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
    
    // Properties
    public override string ProviderName => "Ollama (Local)";
}
```

**Configuration:**
- `baseUrl`: Ollama API endpoint (default: http://localhost:11434)
- `model`: Model name (e.g., "qwen2.5", "llama3.2")

**Error Codes:**
- `OLLAMA_001`: Connection refused - Ollama not running
- `OLLAMA_002`: Model not found - Pull model first
- `OLLAMA_003`: Timeout - Model loading or response timeout

#### LMStudioProvider

```csharp
public class LMStudioProvider : LocalAIProvider
{
    // Similar structure to OllamaProvider
}
```

### Cloud Providers

#### OpenAIProvider

```csharp
public class OpenAIProvider : IAIProvider
{
    // Constructor
    public OpenAIProvider(string apiKey, string model, IHttpClient httpClient, Logger logger)
    
    // Rate Limits
    - GPT-4o: 10,000 TPM, 500 RPM
    - GPT-4o-mini: 200,000 TPM, 10,000 RPM
    - GPT-3.5-turbo: 200,000 TPM, 10,000 RPM
}
```

**Error Codes:**
- `OPENAI_401`: Invalid API key
- `OPENAI_429`: Rate limit exceeded
- `OPENAI_500`: OpenAI service error

#### AnthropicProvider

```csharp
public class AnthropicProvider : IAIProvider
{
    // Supports Claude 3.5 Haiku, Sonnet, and Claude 3 Opus
    // Rate limits vary by tier
}
```

#### Additional Providers

- **PerplexityProvider**: Web-enhanced recommendations
- **OpenRouterProvider**: Gateway to 200+ models
- **DeepSeekProvider**: Cost-effective alternative
- **GeminiProvider**: Google's models with free tier
- **GroqProvider**: Ultra-fast inference

## Core Services

### AIProviderFactory

Creates and configures AI provider instances.

```csharp
public class AIProviderFactory : IProviderFactory
{
    public IAIProvider CreateProvider(BrainarrSettings settings, IHttpClient httpClient, Logger logger)
    {
        // Provider creation logic based on settings.Provider enum
        // Throws ArgumentException if provider not configured
        // Throws NotSupportedException if provider type unknown
    }
}
```

### LibraryAwarePromptBuilder

Builds optimized prompts based on library context.

```csharp
public class LibraryAwarePromptBuilder
{
    /// <summary>
    /// Builds prompt optimized for provider capabilities
    /// </summary>
    /// <param name="profile">Library profile</param>
    /// <param name="settings">Configuration settings</param>
    /// <param name="providerCapabilities">Provider token limits</param>
    /// <returns>Optimized prompt string</returns>
    public string BuildPrompt(
        LibraryProfile profile, 
        BrainarrSettings settings,
        ProviderCapabilities providerCapabilities)
}
```

**Token Optimization:**
- Local models: ~2000 tokens
- Budget providers: ~3000 tokens  
- Premium providers: ~4000 tokens

### IterativeRecommendationStrategy

Handles iterative recommendation fetching to avoid duplicates.

```csharp
public class IterativeRecommendationStrategy
{
    /// <summary>
    /// Gets recommendations iteratively, filtering duplicates
    /// </summary>
    /// <param name="provider">AI provider instance</param>
    /// <param name="profile">Library profile</param>
    /// <param name="existingArtists">Artists already in library</param>
    /// <param name="existingAlbums">Albums already in library</param>
    /// <param name="settings">Configuration</param>
    /// <returns>Unique recommendations</returns>
    public async Task<List<Recommendation>> GetIterativeRecommendationsAsync(
        IAIProvider provider,
        LibraryProfile profile,
        List<Artist> existingArtists,
        List<Album> existingAlbums,
        BrainarrSettings settings)
}
```

**Algorithm:**
1. Request initial batch (2x desired count)
2. Filter duplicates against library
3. If insufficient unique items, request more
4. Maximum 3 iterations to prevent infinite loops

### RateLimiter

Manages API rate limiting per provider.

```csharp
public class RateLimiter : IRateLimiter
{
    /// <summary>
    /// Executes action with rate limiting
    /// </summary>
    public async Task<T> ExecuteAsync<T>(string provider, Func<Task<T>> action)
    
    /// <summary>
    /// Configures rate limits for provider
    /// </summary>
    public void ConfigureLimit(string provider, int requestsPerMinute, int tokensPerMinute)
}
```

**Default Limits:**

| Provider | Requests/Min | Tokens/Min |
|----------|-------------|------------|
| Ollama | Unlimited | Unlimited |
| LMStudio | Unlimited | Unlimited |
| OpenAI | 500 | 200,000 |
| Anthropic | 50 | 100,000 |
| Gemini | 15 | 1,000,000 |
| Groq | 30 | 100,000 |
| DeepSeek | 60 | 500,000 |
| Perplexity | 50 | 100,000 |
| OpenRouter | Varies | Varies |

### ProviderHealthMonitor

Monitors provider availability and performance.

```csharp
public class ProviderHealthMonitor : IProviderHealthMonitor
{
    /// <summary>
    /// Checks provider health status
    /// </summary>
    public async Task<HealthStatus> CheckHealthAsync(string provider, string endpoint)
    
    /// <summary>
    /// Records successful request
    /// </summary>
    public void RecordSuccess(string provider, double responseTimeMs)
    
    /// <summary>
    /// Records failed request
    /// </summary>
    public void RecordFailure(string provider, string error)
    
    /// <summary>
    /// Gets provider metrics
    /// </summary>
    public ProviderMetrics GetMetrics(string provider)
}
```

**Health States:**
- `Healthy`: Provider responding normally
- `Degraded`: Slow responses or occasional failures
- `Unhealthy`: Multiple failures or timeouts

### RecommendationCache

In-memory cache for recommendations.

```csharp
public class RecommendationCache : IRecommendationCache
{
    // Default cache duration: 60 minutes
    // Maximum cache size: 100 entries
    // LRU eviction policy
}
```

## Data Models

### Recommendation

```csharp
public class Recommendation
{
    public string Artist { get; set; }      // Artist name
    public string Album { get; set; }       // Album title
    public string Genre { get; set; }       // Music genre
    public double Confidence { get; set; }  // 0.0-1.0 confidence score
    public string Reason { get; set; }      // Recommendation reasoning
    public int? ReleaseYear { get; set; }   // Optional release year
}
```

### LibraryProfile

```csharp
public class LibraryProfile
{
    public int TotalArtists { get; set; }
    public int TotalAlbums { get; set; }
    public Dictionary<string, int> TopGenres { get; set; }
    public List<string> TopArtists { get; set; }
    public List<string> RecentlyAdded { get; set; }
}
```

### ProviderCapabilities

```csharp
public class ProviderCapabilities
{
    public bool IsLocalModel { get; set; }
    public int MaxTokens { get; set; }
    public int MaxRequestsPerMinute { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool RequiresApiKey { get; set; }
}
```

## Error Handling

### Common Exceptions

```csharp
// Provider not configured
throw new InvalidOperationException($"Provider {providerType} is not configured");

// Invalid API response
throw new InvalidOperationException($"Invalid response from {provider}: {response}");

// Rate limit exceeded
throw new RateLimitException($"Rate limit exceeded for {provider}");

// Connection failure
throw new HttpException($"Failed to connect to {provider}: {error}");
```

### Error Recovery

1. **Automatic Retry**: Exponential backoff with max 3 retries
2. **Provider Failover**: Automatic switch to backup provider
3. **Cache Fallback**: Return cached results if available
4. **Graceful Degradation**: Return empty list rather than crash

## Testing

### Unit Test Helpers

```csharp
public class TestDataGenerator
{
    public static LibraryProfile GenerateLibraryProfile(int artistCount = 100)
    public static List<Recommendation> GenerateRecommendations(int count = 10)
    public static BrainarrSettings GenerateSettings(AIProvider provider)
}
```

### Integration Testing

```csharp
// Test provider connection
var provider = factory.CreateProvider(settings, httpClient, logger);
var connected = await provider.TestConnectionAsync();

// Test recommendation fetching
var recommendations = await provider.GetRecommendationsAsync(prompt);
Assert.That(recommendations, Is.Not.Empty);
```

## Performance Considerations

### Optimization Tips

1. **Enable Caching**: Reduces API calls by 60-80%
2. **Use Local Providers**: Zero latency, no rate limits
3. **Batch Requests**: Process multiple recommendations together
4. **Token Optimization**: Use appropriate sampling strategy
5. **Connection Pooling**: Reuse HTTP connections

### Benchmarks

| Operation | Local (Ollama) | Cloud (OpenAI) | Cloud (DeepSeek) |
|-----------|---------------|----------------|------------------|
| Connection Test | <100ms | 200-500ms | 150-300ms |
| Get 10 Recommendations | 2-5s | 3-8s | 2-4s |
| With Cache Hit | <10ms | <10ms | <10ms |

## Security

### API Key Management

- Stored using Lidarr's secure configuration system
- Never logged or transmitted in plain text
- Validated on configuration save
- Supports environment variable injection

### Network Security

- HTTPS enforced for cloud providers
- Local providers use localhost only
- No telemetry or data collection
- Request/response sanitization

## Versioning

API follows semantic versioning:
- Major: Breaking changes
- Minor: New features, backward compatible
- Patch: Bug fixes

Current Version: 1.0.0

## Support

For issues or questions:
1. Check troubleshooting guide
2. Review error codes above
3. Enable debug logging
4. Report issues on GitHub