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
    Task<LibraryProfile> AnalyzeLibraryAsync(List<Artist> artists);
    string GeneratePromptContext(LibraryProfile profile, DiscoveryMode mode);
}
```

#### Methods

##### AnalyzeLibraryAsync
Analyzes the music library to create a profile.

**Parameters:**
- `artists` (List<Artist>): List of artists in the user's library

**Returns:**
- `Task<LibraryProfile>`: Analyzed library profile with genre distribution, era preferences, etc.

##### GeneratePromptContext
Generates prompt context based on library profile and discovery mode.

**Parameters:**
- `profile` (LibraryProfile): The analyzed library profile
- `mode` (DiscoveryMode): Discovery mode (Similar, Adjacent, or Exploratory)

**Returns:**
- `string`: Generated prompt context for AI providers

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
}
```

#### Properties

- **Artist** (string): The artist name
- **Album** (string): The album title
- **Genre** (string): The music genre
- **Confidence** (double): Confidence score from 0.0 to 1.0
- **Reason** (string): Explanation for why this album was recommended

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
    string model,       // Default: llama3
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

Local AI provider with GUI interface for easy model management.

**Constructor:**
```csharp
public LMStudioProvider(
    string baseUrl,     // Default: http://localhost:1234
    string model,       // Any loaded model
    IHttpClient httpClient,
    Logger logger
)
```

**Features:**
- User-friendly desktop application
- Automatic model downloading from Hugging Face
- GUI for configuration
- Wide model compatibility

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
    Unhealthy
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

- `BR001`: Provider initialization failed
- `BR002`: API key validation failed
- `BR003`: Connection timeout
- `BR004`: Rate limit exceeded
- `BR005`: Invalid response format
- `BR006`: Model not found
- `BR007`: Insufficient quota
- `BR008`: Provider health check failed

---

## Usage Examples

### Basic Usage

```csharp
// Initialize a provider
var provider = new OllamaProvider(
    "http://localhost:11434",
    "llama3",
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
var analyzer = new LibraryAnalyzer(logger);

// Analyze library
var profile = await analyzer.AnalyzeLibraryAsync(artists);

// Generate context for different discovery modes
var similarContext = analyzer.GeneratePromptContext(profile, DiscoveryMode.Similar);
var exploratoryContext = analyzer.GeneratePromptContext(profile, DiscoveryMode.Exploratory);

// Use with provider
var recommendations = await provider.GetRecommendationsAsync(similarContext);
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
settings.ModelName = "llama3";
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
- Initial release with 9 AI providers
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