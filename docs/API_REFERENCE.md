<!-- markdownlint-disable MD022 MD032 MD031 MD024 MD029 MD036 MD034 MD026 MD047 -->
# Brainarr API Reference

## Core Interfaces

### IAIProvider

The primary interface that all AI providers must implement to integrate with Brainarr.

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
Gets music recommendations based on the provided prompt.

**Parameters:**
- `prompt` (string): The prompt containing user's music library and preferences

**Returns:**
- `Task<List<Recommendation>>`: A list of recommended albums with metadata

**Example:**
```csharp
var provider = new OllamaProvider(baseUrl, model, httpClient, logger);
var recommendations = await provider.GetRecommendationsAsync(
    "Based on my library of Pink Floyd, Led Zeppelin, recommend similar albums"
);
```

##### TestConnectionAsync
Tests the connection to the AI provider.

**Parameters:**
- None

**Returns:**
- `Task<bool>`: True if the connection is successful; otherwise, false

**Example:**
```csharp
if (await provider.TestConnectionAsync())
{
    logger.Info("Provider is available");
}
```

#### Properties

##### ProviderName
Gets the display name of the provider.

**Type:** `string`
**Access:** Read-only

##### UpdateModel
Updates the model used by the provider dynamically.

**Parameters:**
- `modelName` (string): The new model name to use

**Returns:**
- `void`

**Example:**
```csharp
// Switch from default model to a more powerful one
provider.UpdateModel("llama3.1:70b");
```

**Notes:**
- Not all providers support dynamic model updates
- Model name must be valid for the provider
- Throws `InvalidOperationException` if model is not available

---

### IAIService

Main orchestrator service for AI providers. Manages provider chains, failover, and health monitoring.

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

**Parameters:**
- `prompt` (string): The prompt to send to AI providers

**Returns:**
- `Task<List<Recommendation>>`: List of recommendations from the first successful provider

**Behavior:**
- Attempts providers in priority order
- Automatically falls back to secondary providers on failure
- Caches successful responses based on configuration

##### TestAllProvidersAsync
Tests connectivity to all configured providers.

**Returns:**
- `Task<Dictionary<string, bool>>`: Dictionary of provider names and their connection status

##### GetProviderHealthAsync
Gets the health status of all providers.

**Returns:**
- `Task<Dictionary<string, ProviderHealthInfo>>`: Dictionary of provider names and their health information

##### RegisterProvider
Registers a new provider with the service.

**Parameters:**
- `provider` (IAIProvider): Provider to register
- `priority` (int): Priority in the failover chain (lower = higher priority, default: 100)

##### UpdateConfiguration
Updates the configuration for all providers.

**Parameters:**
- `settings` (BrainarrSettings): Updated settings

##### GetMetrics
Gets metrics for all provider usage.

**Returns:**
- `AIServiceMetrics`: Provider usage metrics including request counts, response times, and error rates

---

### ILibraryAnalyzer

Analyzes the user's music library to generate context for AI recommendations.

```csharp
public interface ILibraryAnalyzer
{
    LibraryProfile AnalyzeLibrary();
    string BuildPrompt(LibraryProfile profile, int maxRecommendations, DiscoveryMode discoveryMode);
    List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations);
}
```

#### Methods

##### AnalyzeLibrary
Analyzes the music library to create a profile.

**Parameters:**
- None - retrieves data directly from Lidarr services

**Returns:**
- `LibraryProfile`: Analyzed library profile with genre distribution, era preferences, etc.

##### BuildPrompt
Builds a prompt for AI recommendations based on the library profile.

**Parameters:**
- `profile` (LibraryProfile): The analyzed library profile
- `maxRecommendations` (int): Maximum number of recommendations to request
- `discoveryMode` (DiscoveryMode): Discovery mode (Similar, Adjacent, or Exploratory)

**Returns:**
- `string`: Formatted prompt string for AI providers

##### FilterDuplicates
Filters recommendations to remove duplicates already in the library.

**Parameters:**
- `recommendations` (List<ImportListItemInfo>): List of recommendations from AI

**Returns:**
- `List<ImportListItemInfo>`: Filtered list without duplicates

---

## Data Models

### Recommendation

Represents a music recommendation with metadata.

```csharp
public class Recommendation
{
    public string Artist { get; set; }
    public string Album { get; set; }
    public string Genre { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; }
    public int? Year { get; set; }                   // Preferred: Release year of the album
    public int? ReleaseYear { get; set; }            // Alternative property name for Year
    public string Source { get; set; }               // Source provider identifier
    public string Provider { get; set; }             // Provider that made the recommendation
    public string MusicBrainzId { get; set; }        // MusicBrainz ID for the recommendation
    public string ArtistMusicBrainzId { get; set; }  // Artist MusicBrainz ID
    public string AlbumMusicBrainzId { get; set; }   // Album MusicBrainz ID
    public string SpotifyId { get; set; }            // Spotify ID for the album
}
```

#### Properties

- **Artist** (string): The artist name
- **Album** (string): The album title
- **Genre** (string): The music genre (optional)
- **Confidence** (double): Confidence score from 0.0 to 1.0
- **Reason** (string): Explanation for why this album was recommended (optional)
- **Year** (int?): Release year of the album (preferred property)
- **ReleaseYear** (int?): Alternative property name for Year (optional)
- **Source** (string): Source provider identifier (optional)
- **Provider** (string): Provider that made this recommendation (optional)
- **MusicBrainzId** (string): MusicBrainz ID for the recommendation (optional)
- **ArtistMusicBrainzId** (string): Artist MusicBrainz ID (optional)
- **AlbumMusicBrainzId** (string): Album MusicBrainz ID (optional)
- **SpotifyId** (string): Spotify ID for the album (optional)

---

### ProviderHealthInfo

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

#### Properties

- **ProviderName** (string): Name of the provider
- **Status** (HealthStatus): Current health status (Healthy, Degraded, Unhealthy)
- **SuccessRate** (double): Success rate as a percentage (0-100)
- **AverageResponseTime** (double): Average response time in milliseconds
- **TotalRequests** (int): Total number of requests made
- **FailedRequests** (int): Number of failed requests
- **LastError** (string): Description of the last error encountered
- **IsAvailable** (bool): Whether the provider is currently available

---

### AIServiceMetrics

Metrics for provider usage.

```csharp
public class AIServiceMetrics
{
    public Dictionary<string, int> RequestCounts { get; set; }
    public Dictionary<string, double> AverageResponseTimes { get; set; }
    public Dictionary<string, int> ErrorCounts { get; set; }
    public Dictionary<string, long> TotalTokensUsed { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
}
```

#### Properties

- **RequestCounts**: Number of requests per provider
- **AverageResponseTimes**: Average response time per provider (ms)
- **ErrorCounts**: Number of errors per provider
- **TotalTokensUsed**: Total tokens consumed per provider
- **TotalRequests**: Total requests across all providers
- **SuccessfulRequests**: Total successful requests
- **FailedRequests**: Total failed requests

---

## Provider Implementations

### Local Providers

#### OllamaProvider

Local AI provider using Ollama for privacy-focused recommendations.

**Constructor:**
```csharp
public OllamaProvider(
    string baseUrl,     // Default: http://localhost:11434
    string model,       // Default: qwen2.5:latest
    IHttpClient httpClient,
    Logger logger
)
```

**Features:**
- 100% local processing
- No data leaves your network
- Supports multiple open-source models
- Automatic model detection

#### LMStudioProvider

Dedicated local AI provider implementation with GUI interface for easy model management. This is a separate implementation from OllamaProvider, specifically designed for LM Studio's OpenAI-compatible API.

**Constructor:**
```csharp
public LMStudioProvider(
    string baseUrl,     // Default: http://localhost:1234
    string model,       // Default: local-model (or any loaded model)
    IHttpClient httpClient,
    Logger logger
)
```

**Features:**
- User-friendly desktop application
- Uses OpenAI-compatible endpoint (`/v1/chat/completions`)
- Automatic model downloading from Hugging Face
- GUI for configuration
- Wide model compatibility

**Implementation Note:**
LMStudioProvider is a dedicated class in `LocalAIProvider.cs` that uses the OpenAI-compatible API format, while OllamaProvider uses Ollama's native API format.

### Cloud Providers

#### OpenAIProvider

OpenAI provider for GPT model recommendations.

**Constructor:**
```csharp
public OpenAIProvider(
    IHttpClient httpClient,
    Logger logger,
    string apiKey,      // Required
    string model        // Default: gpt-4o-mini
)
```

**Supported Models:**
- gpt-4o
- gpt-4o-mini
- gpt-4-turbo
- gpt-3.5-turbo

#### AnthropicProvider

Anthropic provider for Claude model recommendations.

**Constructor:**
```csharp
public AnthropicProvider(
    IHttpClient httpClient,
    Logger logger,
    string apiKey,      // Required
    string model        // Default: claude-3-5-haiku-latest
)
```

**Supported Models:**
- claude-3-5-sonnet-latest
- claude-3-5-haiku-latest
- claude-3-opus-latest

#### GeminiProvider

Google Gemini provider with free tier support.

**Constructor:**
```csharp
public GeminiProvider(
    IHttpClient httpClient,
    Logger logger,
    string apiKey,      // Required (free at aistudio.google.com)
    string model        // Default: gemini-1.5-flash
)
```

**Supported Models:**
- gemini-1.5-flash (free tier)
- gemini-1.5-pro
- gemini-1.5-pro-002 (2M context)

#### Additional Providers

- **DeepSeekProvider**: Budget-friendly with 10-20x lower costs
- **GroqProvider**: Ultra-fast inference speeds
- **OpenRouterProvider**: Gateway to 200+ models
- **PerplexityProvider**: Web-enhanced responses

---

## Configuration

### BrainarrSettings

Main configuration class for the plugin.

```csharp
public class BrainarrSettings
{
    // Provider Configuration
    public AIProvider Provider { get; set; }
    public string ApiKey { get; set; }
    public string ModelName { get; set; }

    // Discovery Settings
    public DiscoveryMode DiscoveryMode { get; set; }
    public int MaxRecommendations { get; set; }
    public double MinimumConfidence { get; set; }

    // Performance Settings
    public int CacheDurationMinutes { get; set; }
    public bool EnableAutoDetection { get; set; }
    public int RequestTimeoutSeconds { get; set; }

    // Advanced Settings
    public List<string> ProviderChain { get; set; }
    public bool EnableHealthMonitoring { get; set; }
    public bool LogProviderRequests { get; set; }
}
```

### Enumerations

#### AIProvider
```csharp
public enum AIProvider
{
    Ollama,
    LMStudio,
    OpenAI,
    Anthropic,
    Gemini,
    DeepSeek,
    Groq,
    OpenRouter,
    Perplexity
}
```

#### DiscoveryMode
```csharp
public enum DiscoveryMode
{
    Similar,     // Very similar to existing library
    Adjacent,    // Related genres and styles
    Exploratory  // New genres and territories
}
```

#### HealthStatus
```csharp
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
```

---

## Error Handling

### Common Exceptions

#### ProviderNotFoundException
Thrown when a specified provider cannot be found or initialized.

#### ProviderUnavailableException
Thrown when all providers in the chain are unavailable.

#### InvalidApiKeyException
Thrown when an API key is invalid or missing for cloud providers.

#### RateLimitExceededException
Thrown when provider rate limits are exceeded.

### Error Codes

- `BR001`: Provider initialization failed - The AI provider could not be initialized. Check provider configuration and dependencies.
- `BR002`: API key validation failed - The provided API key is invalid or expired. Verify your API key in settings.
- `BR003`: Connection timeout - Request to provider timed out. Check network connectivity and increase timeout if needed.
- `BR004`: Rate limit exceeded - Provider's rate limit has been reached. Wait before retrying or upgrade your plan.
- `BR005`: Invalid response format - Provider returned unexpected response format. Check provider version compatibility.
- `BR006`: Model not found - Specified model is not available for this provider. Use GetAvailableModelsAsync() to list valid models.
- `BR007`: Insufficient quota - API quota exhausted. Check your provider account balance or usage limits.
- `BR008`: Provider health check failed - Provider is unhealthy or unavailable. Check provider status and logs.

---

## Usage Examples

### Basic Usage

```csharp
// Initialize a provider
var provider = new OllamaProvider(
    "http://localhost:11434",
    "qwen2.5:latest",
    httpClient,
    logger
);

// Test connection
if (await provider.TestConnectionAsync())
{
    // Get recommendations
    var prompt = "I love Pink Floyd and Led Zeppelin";
    var recommendations = await provider.GetRecommendationsAsync(prompt);

    foreach (var rec in recommendations)
    {
        Console.WriteLine($"{rec.Artist} - {rec.Album} ({rec.Confidence:P})");
    }
}
```

### Using AIService with Failover

```csharp
// Create service with multiple providers
var aiService = new AIService(httpClient, logger, settings);

// Register providers with priorities
aiService.RegisterProvider(ollamaProvider, priority: 1);
aiService.RegisterProvider(openAIProvider, priority: 2);
aiService.RegisterProvider(anthropicProvider, priority: 3);

// Get recommendations with automatic failover
var recommendations = await aiService.GetRecommendationsAsync(prompt);

// Check provider health
var health = await aiService.GetProviderHealthAsync();
foreach (var (provider, info) in health)
{
    Console.WriteLine($"{provider}: {info.Status} ({info.SuccessRate:P})");
}
```

### Library Analysis

```csharp
var analyzer = new LibraryAnalyzer(artistService, albumService, logger);

// Analyze library
var profile = analyzer.AnalyzeLibrary();

// Generate context for different discovery modes
var similarContext = analyzer.BuildPrompt(profile, 20, DiscoveryMode.Similar);
var exploratoryContext = analyzer.BuildPrompt(profile, 20, DiscoveryMode.Exploratory);

// Use with provider
var recommendations = await provider.GetRecommendationsAsync(similarContext);

// Filter out duplicates
var filtered = analyzer.FilterDuplicates(recommendations);
```

---

## Best Practices

### Provider Selection

1. **Privacy-First**: Use Ollama or LM Studio for complete data privacy
2. **Cost-Effective**: Use DeepSeek or Gemini free tier for budget constraints
3. **Quality-First**: Use OpenAI GPT-4 or Anthropic Claude for best results
4. **Speed-First**: Use Groq for fastest response times

### Performance Optimization

1. **Enable Caching**: Set appropriate cache duration (30-60 minutes recommended)
2. **Use Provider Chains**: Configure fallback providers for reliability
3. **Monitor Health**: Enable health monitoring to track provider performance
4. **Optimize Prompts**: Use concise, focused prompts to reduce token usage

### Error Handling

1. **Implement Retries**: Use exponential backoff for transient failures
2. **Log Errors**: Enable diagnostic logging for troubleshooting
3. **Handle Rate Limits**: Implement rate limiting per provider
4. **Validate Responses**: Check response format before processing

### Security

1. **Protect API Keys**: Never commit keys to source control
2. **Use Environment Variables**: Store keys in environment variables
3. **Rotate Keys**: Regularly rotate API keys for cloud providers
4. **Monitor Usage**: Track API usage to detect anomalies

---

## Troubleshooting

### Provider Not Detected

```csharp
// Enable auto-detection
settings.EnableAutoDetection = true;

// Or manually specify provider
settings.Provider = AIProvider.Ollama;
settings.ModelName = "qwen2.5:latest";
```

### Connection Issues

```csharp
// Increase timeout
settings.RequestTimeoutSeconds = 120;

// Enable detailed logging
settings.LogProviderRequests = true;

// Test specific provider
var isAvailable = await provider.TestConnectionAsync();
```

### Poor Recommendations

```csharp
// Adjust discovery mode
settings.DiscoveryMode = DiscoveryMode.Similar;

// Increase minimum confidence
settings.MinimumConfidence = 0.7;

// Use better model
settings.ModelName = "gpt-4o"; // or "claude-3-5-sonnet-latest"
```

---

## Version History

### v1.0.0
- Initial release with 8 AI providers
- Multi-provider support with failover
- Health monitoring and metrics
- Comprehensive caching system
- Library analysis and profiling

---

## Additional Resources

- [Architecture Documentation](ARCHITECTURE.md)
- [Provider Setup Guide](PROVIDER_GUIDE.md)
- [User Setup Guide](USER_SETUP_GUIDE.md)
- [Contributing Guide](../CONTRIBUTING.md)
- [Development Guide](../DEVELOPMENT.md)

<!-- markdownlint-enable MD022 MD032 MD031 MD024 MD029 MD036 MD034 MD026 MD047 -->
---

## Provider UI Actions

Certain UI operations are handled via provider actions without changing existing contracts. These actions are invoked by the UI layer and routed to .

- : 
  - Returns 

- : 
  - Returns 
  - Purpose: provide structured connection test details alongside a user-facing hint when available (e.g., Google Gemini SERVICE_DISABLED activation URL).
  - Notes:  is provider-specific and may be null.  links to the relevant wiki/GitHub docs section when available.

- : 
  - Returns 
  - Purpose: provide copy-paste curl commands to sanity-check connectivity outside Brainarr (never includes real keys; uses placeholders like ).
  - Examples: Gemini model list, OpenAI/Anthropic/OpenRouter model list, local Ollama/LM Studio endpoints.


### Example: Test With Learn More Link (frontend)

```ts
async function testConnectionDetails(settings: any) {
  const res = await fetch('/api/v1/brainarr/provider/action', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ action: 'testconnection/details', ...settings })
  });
  const data = await res.json(); // { success, provider, hint, message, docs }
  if (!data.success) {
    console.error(data.message, data.hint, data.docs);
  }
  return data;
}
```

Render hint + Learn more when present (docs URL points to GitHub docs):

```tsx
{result && !result.success && (
  <div>
    <div>{result.message || `Cannot connect to ${result.provider}`}</div>
    {result.hint && <div>{result.hint}</div>}
    {result.docs && (
      <div>
        <a href={result.docs} target="_blank" rel="noreferrer">Learn more</a>
      </div>
    )}
  </div>
)}
```
