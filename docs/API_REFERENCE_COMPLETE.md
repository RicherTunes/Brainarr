# Brainarr Complete API Reference

## Table of Contents
1. [Core Interfaces](#core-interfaces)
2. [Service Interfaces](#service-interfaces)
3. [Provider Interfaces](#provider-interfaces)
4. [Configuration Classes](#configuration-classes)
5. [Models and DTOs](#models-and-dtos)
6. [Request/Response Examples](#requestresponse-examples)
7. [Error Handling](#error-handling)
8. [Extension Points](#extension-points)

---

## Core Interfaces

### IAIProvider

Primary interface that all AI providers must implement.

```csharp
public interface IAIProvider
{
    Task<List<Recommendation>> GetRecommendationsAsync(string prompt);
    Task<bool> TestConnectionAsync();
    string ProviderName { get; }
    void UpdateModel(string modelName);
}
```

#### Methods

##### GetRecommendationsAsync
Generates music recommendations based on the provided prompt.

**Parameters:**
- `prompt` (string): Contains user's music library context and preferences

**Returns:**
- `Task<List<Recommendation>>`: List of recommended albums with metadata

**Example:**
```csharp
var provider = new OllamaProvider(baseUrl, model, httpClient, logger);
var recommendations = await provider.GetRecommendationsAsync(
    "User has 50 artists including Pink Floyd, Led Zeppelin. Prefers classic rock. Discovery mode: Adjacent"
);

// Returns:
[
  {
    "artist": "King Crimson",
    "album": "In the Court of the Crimson King",
    "genre": "Progressive Rock",
    "confidence": 0.92,
    "reason": "Progressive rock pioneer similar to Pink Floyd's experimental works"
  }
]
```

##### TestConnectionAsync
Tests the connection and availability of the AI provider.

**Returns:**
- `Task<bool>`: True if provider is accessible and configured correctly

**Example:**
```csharp
if (await provider.TestConnectionAsync())
{
    _logger.Info($"{provider.ProviderName} is available");
}
else
{
    _logger.Warn($"{provider.ProviderName} connection failed");
}
```

##### UpdateModel
Dynamically updates the AI model used by the provider.

**Parameters:**
- `modelName` (string): Name of the model to use

**Throws:**
- `InvalidOperationException`: If model is not available
- `NotSupportedException`: If provider doesn't support model switching

**Example:**
```csharp
// Switch to a more powerful model
provider.UpdateModel("llama3.2:70b");

// For cloud providers
provider.UpdateModel("gpt-4o");  // OpenAI
provider.UpdateModel("claude-3-5-sonnet-latest");  // Anthropic
```

---

### IAIService

Main orchestrator for AI providers with failover and health monitoring.

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

#### Methods

##### GetRecommendationsAsync
Gets recommendations using the configured provider chain with automatic failover.

**Failover Logic:**
1. Attempts primary provider
2. On failure, tries secondary providers in priority order
3. Implements exponential backoff retry
4. Returns cached results if all providers fail

**Example:**
```csharp
var service = new AIService(logger, healthMonitor, retryPolicy, rateLimiter, sanitizer, validator);
service.RegisterProvider(ollamaProvider, priority: 1);
service.RegisterProvider(openAiProvider, priority: 2);

var recommendations = await service.GetRecommendationsAsync(prompt);
// Automatically handles failover if Ollama fails
```

##### GetProviderHealthAsync
Returns detailed health information for all registered providers.

**Returns:**
```csharp
{
  "Ollama": {
    "Status": "Healthy",
    "LastCheck": "2024-01-15T10:30:00Z",
    "ResponseTime": 250,
    "SuccessRate": 0.98,
    "LastError": null
  },
  "OpenAI": {
    "Status": "Degraded",
    "LastCheck": "2024-01-15T10:29:00Z",
    "ResponseTime": 1500,
    "SuccessRate": 0.85,
    "LastError": "Rate limit exceeded"
  }
}
```

---

## Service Interfaces

### IBrainarrOrchestrator

Main orchestration interface for the import list functionality.

```csharp
public interface IBrainarrOrchestrator
{
    Task<IList<ImportListItemInfo>> FetchRecommendationsAsync(BrainarrSettings settings);
    Task<ValidationResult> ValidateSettingsAsync(BrainarrSettings settings);
    Task<LibraryProfile> AnalyzeLibraryAsync(IEnumerable<Artist> artists);
    void UpdateCacheSettings(CacheConfiguration config);
}
```

### ILibraryAnalyzer

Analyzes user's music library to build recommendation context.

```csharp
public interface ILibraryAnalyzer
{
    Task<LibraryProfile> AnalyzeAsync(IEnumerable<Artist> artists);
    Task<GenreDistribution> GetGenreDistributionAsync(IEnumerable<Artist> artists);
    Task<List<string>> GetTopGenresAsync(IEnumerable<Artist> artists, int count = 5);
    Task<EraPreference> AnalyzeEraPreferenceAsync(IEnumerable<Artist> artists);
}
```

**LibraryProfile Structure:**
```csharp
public class LibraryProfile
{
    public int TotalArtists { get; set; }
    public int TotalAlbums { get; set; }
    public Dictionary<string, float> GenreDistribution { get; set; }
    public List<string> TopArtists { get; set; }
    public List<string> TopGenres { get; set; }
    public EraPreference EraPref { get; set; }
    public DiversityScore Diversity { get; set; }
    public string Summary { get; set; }
}
```

### IRecommendationSanitizer

Sanitizes and validates AI responses.

```csharp
public interface IRecommendationSanitizer
{
    List<Recommendation> SanitizeRecommendations(List<Recommendation> recommendations);
    Recommendation SanitizeRecommendation(Recommendation recommendation);
    bool IsValidArtistName(string artist);
    bool IsValidAlbumName(string album);
    string SanitizeText(string text);
}
```

### IProviderManager

Manages provider lifecycle and configuration.

```csharp
public interface IProviderManager
{
    void RegisterProvider(string name, IAIProvider provider);
    void UnregisterProvider(string name);
    IAIProvider GetProvider(string name);
    IEnumerable<IAIProvider> GetAllProviders();
    Task<Dictionary<string, bool>> TestAllProvidersAsync();
    void SetPrimaryProvider(string name);
    void SetFallbackChain(params string[] providerNames);
}
```

### IModelDetectionService

Auto-detects available models for local providers.

```csharp
public interface IModelDetectionService
{
    Task<List<ModelInfo>> DetectOllamaModelsAsync(string baseUrl);
    Task<List<ModelInfo>> DetectLMStudioModelsAsync(string baseUrl);
    Task<bool> IsModelAvailableAsync(string provider, string model);
    Task<ModelCapabilities> GetModelCapabilitiesAsync(string provider, string model);
}
```

**ModelInfo Structure:**
```csharp
public class ModelInfo
{
    public string Name { get; set; }
    public string Version { get; set; }
    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public List<string> Capabilities { get; set; }
}
```

---

## Provider Interfaces

### IProviderCapabilities

Defines provider capabilities for feature detection.

```csharp
public interface IProviderCapabilities
{
    bool SupportsStreaming { get; }
    bool SupportsModelSwitching { get; }
    bool SupportsSystemPrompt { get; }
    bool SupportsTools { get; }
    int MaxContextLength { get; }
    int MaxOutputTokens { get; }
    List<string> SupportedModels { get; }
    Dictionary<string, decimal> ModelPricing { get; }
}
```

### IProviderHealth

Health monitoring interface for providers.

```csharp
public interface IProviderHealth
{
    HealthStatus Status { get; }
    DateTime LastHealthCheck { get; }
    int ConsecutiveFailures { get; }
    double AverageResponseTime { get; }
    double SuccessRate { get; }
    string LastError { get; }
    Task<HealthCheckResult> CheckHealthAsync();
}
```

---

## Configuration Classes

### BrainarrSettings

Main configuration class for the plugin.

```csharp
public class BrainarrSettings : IProviderConfig
{
    // Provider Selection
    [FieldDefinition(1, Label = "AI Provider", Type = FieldType.Select)]
    public AIProvider Provider { get; set; }
    
    // Discovery Settings
    [FieldDefinition(10, Label = "Discovery Mode", Type = FieldType.Select)]
    public DiscoveryMode DiscoveryMode { get; set; }
    
    [FieldDefinition(11, Label = "Max Recommendations")]
    public int MaxRecommendations { get; set; } = 20;
    
    [FieldDefinition(12, Label = "Minimum Confidence")]
    public double MinimumConfidence { get; set; } = 0.5;
    
    // Provider-Specific Settings (shown conditionally)
    [FieldDefinition(20, Label = "API Key", Type = FieldType.Password)]
    public string ApiKey { get; set; }
    
    [FieldDefinition(21, Label = "Model")]
    public string Model { get; set; }
    
    [FieldDefinition(22, Label = "Base URL")]
    public string BaseUrl { get; set; }
    
    // Advanced Settings
    [FieldDefinition(30, Label = "Cache Duration (minutes)", Advanced = true)]
    public int CacheDuration { get; set; } = 60;
    
    [FieldDefinition(31, Label = "Enable Debug Logging", Advanced = true)]
    public bool DebugMode { get; set; }
}
```

### ProviderConfiguration

Provider-specific configuration validation.

```csharp
public class ProviderConfigurationValidator : AbstractValidator<BrainarrSettings>
{
    public ProviderConfigurationValidator()
    {
        // Ollama validation
        When(settings => settings.Provider == AIProvider.Ollama, () =>
        {
            RuleFor(s => s.BaseUrl)
                .NotEmpty().WithMessage("Ollama URL is required")
                .Must(BeValidUrl).WithMessage("Invalid URL format");
        });
        
        // OpenAI validation
        When(settings => settings.Provider == AIProvider.OpenAI, () =>
        {
            RuleFor(s => s.ApiKey)
                .NotEmpty().WithMessage("OpenAI API key is required")
                .Matches(@"^sk-[a-zA-Z0-9]{48}$").WithMessage("Invalid OpenAI API key format");
        });
        
        // Common validation
        RuleFor(s => s.MaxRecommendations)
            .InclusiveBetween(1, 100).WithMessage("Max recommendations must be between 1 and 100");
    }
}
```

---

## Models and DTOs

### Recommendation

Core recommendation model.

```csharp
public class Recommendation
{
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Genre { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string MusicBrainzId { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

### ImportListItemInfo

Lidarr import list item format.

```csharp
public class ImportListItemInfo
{
    public string Artist { get; set; }
    public string Album { get; set; }
    public string ArtistMusicBrainzId { get; set; }
    public string AlbumMusicBrainzId { get; set; }
    public DateTime? ReleaseDate { get; set; }
}
```

### ServiceResult<T>

Generic result wrapper for service operations.

```csharp
public class ServiceResult<T>
{
    public bool Success { get; set; }
    public T Data { get; set; }
    public string Error { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    
    public static ServiceResult<T> Ok(T data) => new ServiceResult<T> 
    { 
        Success = true, 
        Data = data 
    };
    
    public static ServiceResult<T> Fail(string error) => new ServiceResult<T> 
    { 
        Success = false, 
        Error = error 
    };
}
```

---

## Request/Response Examples

### Ollama Provider

**Request:**
```json
POST http://localhost:11434/api/generate
{
  "model": "llama3.2:latest",
  "prompt": "Based on a library containing Pink Floyd, Led Zeppelin, and Deep Purple, recommend 5 similar albums. Return as JSON array with fields: artist, album, genre, confidence (0-1), reason.",
  "stream": false,
  "options": {
    "temperature": 0.7,
    "top_p": 0.9,
    "max_tokens": 2000
  }
}
```

**Response:**
```json
{
  "response": "[{\"artist\":\"King Crimson\",\"album\":\"In the Court of the Crimson King\",\"genre\":\"Progressive Rock\",\"confidence\":0.92,\"reason\":\"Pioneering progressive rock with complex compositions like Pink Floyd\"}]",
  "done": true,
  "context": [...]
}
```

### OpenAI Provider

**Request:**
```json
POST https://api.openai.com/v1/chat/completions
{
  "model": "gpt-4o-mini",
  "messages": [
    {
      "role": "system",
      "content": "You are a music recommendation expert. Provide recommendations as JSON."
    },
    {
      "role": "user",
      "content": "Based on a library with Pink Floyd and Led Zeppelin, recommend 5 albums."
    }
  ],
  "temperature": 0.8,
  "max_tokens": 2000
}
```

**Response:**
```json
{
  "choices": [
    {
      "message": {
        "content": "[{\"artist\":\"Yes\",\"album\":\"Close to the Edge\",\"genre\":\"Progressive Rock\",\"confidence\":0.88,\"reason\":\"Complex progressive rock arrangements similar to Pink Floyd\"}]"
      }
    }
  ],
  "usage": {
    "prompt_tokens": 45,
    "completion_tokens": 150,
    "total_tokens": 195
  }
}
```

### Anthropic Provider

**Request:**
```json
POST https://api.anthropic.com/v1/messages
{
  "model": "claude-3-5-haiku-latest",
  "messages": [
    {
      "role": "user",
      "content": "Based on a rock music library, provide 5 album recommendations as JSON."
    }
  ],
  "max_tokens": 2000,
  "temperature": 0.8,
  "system": "You are a music expert. Return only valid JSON arrays."
}
```

---

## Error Handling

### Error Response Format

```csharp
public class ErrorResponse
{
    public string Error { get; set; }
    public string ErrorCode { get; set; }
    public string Provider { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Details { get; set; }
}
```

### Common Error Codes

| Code | Description | HTTP Status | Resolution |
|------|-------------|-------------|------------|
| `PROVIDER_UNAVAILABLE` | Provider not responding | 503 | Check provider status |
| `INVALID_API_KEY` | Authentication failed | 401 | Verify API key |
| `RATE_LIMIT_EXCEEDED` | Too many requests | 429 | Wait and retry |
| `INVALID_MODEL` | Model not found | 400 | Check model name |
| `TIMEOUT` | Request timeout | 408 | Increase timeout or retry |
| `PARSE_ERROR` | Invalid response format | 500 | Check provider version |
| `INSUFFICIENT_CONTEXT` | Not enough library data | 400 | Add more artists |
| `QUOTA_EXCEEDED` | API quota exhausted | 402 | Check billing |

### Error Handling Examples

```csharp
try
{
    var recommendations = await provider.GetRecommendationsAsync(prompt);
}
catch (ProviderUnavailableException ex)
{
    _logger.Error($"Provider {ex.Provider} unavailable: {ex.Message}");
    // Trigger failover
}
catch (RateLimitException ex)
{
    _logger.Warn($"Rate limit hit, retry after {ex.RetryAfter} seconds");
    await Task.Delay(ex.RetryAfter * 1000);
    // Retry
}
catch (InvalidApiKeyException ex)
{
    _logger.Error("Invalid API key provided");
    // Notify user to check settings
}
```

---

## Extension Points

### Custom Provider Implementation

```csharp
public class CustomProvider : IAIProvider
{
    private readonly IHttpClient _httpClient;
    private readonly Logger _logger;
    
    public string ProviderName => "CustomAI";
    
    public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
    {
        // 1. Prepare request
        var request = BuildRequest(prompt);
        
        // 2. Send to AI service
        var response = await _httpClient.ExecuteAsync(request);
        
        // 3. Parse response
        var recommendations = ParseResponse(response.Content);
        
        // 4. Validate and return
        return ValidateRecommendations(recommendations);
    }
    
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/health");
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }
    
    public void UpdateModel(string modelName)
    {
        _currentModel = modelName;
    }
}
```

### Custom Recommendation Validator

```csharp
public class CustomValidator : IRecommendationValidator
{
    public async Task<ValidationResult> ValidateAsync(Recommendation recommendation)
    {
        var result = new ValidationResult();
        
        // Custom validation logic
        if (string.IsNullOrEmpty(recommendation.Artist))
        {
            result.AddError("Artist name is required");
        }
        
        if (recommendation.Confidence < 0 || recommendation.Confidence > 1)
        {
            result.AddError("Confidence must be between 0 and 1");
        }
        
        // MusicBrainz validation
        if (!string.IsNullOrEmpty(recommendation.MusicBrainzId))
        {
            var isValid = await ValidateMusicBrainzId(recommendation.MusicBrainzId);
            if (!isValid)
            {
                result.AddWarning("Invalid MusicBrainz ID");
            }
        }
        
        return result;
    }
}
```

### Custom Library Analyzer

```csharp
public class CustomLibraryAnalyzer : ILibraryAnalyzer
{
    public async Task<LibraryProfile> AnalyzeAsync(IEnumerable<Artist> artists)
    {
        var profile = new LibraryProfile();
        
        // Analyze genre distribution
        profile.GenreDistribution = CalculateGenreDistribution(artists);
        
        // Analyze era preferences
        profile.EraPref = AnalyzeEraPreference(artists);
        
        // Calculate diversity score
        profile.Diversity = CalculateDiversity(artists);
        
        // Generate summary
        profile.Summary = GenerateSummary(profile);
        
        return profile;
    }
}
```

---

## Performance Considerations

### Caching Strategy

```csharp
public interface IRecommendationCache
{
    Task<List<Recommendation>> GetAsync(string key);
    Task SetAsync(string key, List<Recommendation> recommendations, TimeSpan expiration);
    Task<bool> ExistsAsync(string key);
    Task RemoveAsync(string key);
    Task ClearAsync();
    CacheStatistics GetStatistics();
}
```

### Rate Limiting

```csharp
public interface IRateLimiter
{
    Task<bool> TryAcquireAsync(string provider);
    Task<RateLimitInfo> GetLimitInfoAsync(string provider);
    void Configure(string provider, RateLimitConfig config);
}

public class RateLimitConfig
{
    public int RequestsPerMinute { get; set; }
    public int BurstSize { get; set; }
    public TimeSpan Window { get; set; }
}
```

### Metrics Collection

```csharp
public class AIServiceMetrics
{
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public Dictionary<string, long> RequestsByProvider { get; set; }
    public double AverageResponseTime { get; set; }
    public Dictionary<string, double> ResponseTimeByProvider { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public DateTime LastReset { get; set; }
}
```

---

## Testing Interfaces

### Mock Provider for Testing

```csharp
public class MockAIProvider : IAIProvider
{
    private readonly List<Recommendation> _mockRecommendations;
    
    public MockAIProvider(List<Recommendation> recommendations = null)
    {
        _mockRecommendations = recommendations ?? GenerateDefaultRecommendations();
    }
    
    public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
    {
        return Task.FromResult(_mockRecommendations);
    }
    
    public Task<bool> TestConnectionAsync()
    {
        return Task.FromResult(true);
    }
    
    public string ProviderName => "Mock";
    
    public void UpdateModel(string modelName) { }
}
```

---

## Migration Interfaces

### Settings Migration

```csharp
public interface ISettingsMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    Task MigrateAsync(Dictionary<string, object> oldSettings);
}
```

---

## Security Interfaces

### ISecureApiKeyStorage

```csharp
public interface ISecureApiKeyStorage
{
    void StoreApiKey(string provider, string apiKey);
    SecureString GetApiKey(string provider);
    string GetApiKeyForRequest(string provider);
    void ClearApiKey(string provider);
    void ClearAllApiKeys();
}
```

For complete security documentation, see [SECURITY_ARCHITECTURE.md](./SECURITY_ARCHITECTURE.md)