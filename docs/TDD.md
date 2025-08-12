# Brainarr: Multi-Provider AI Import List Plugin - Enhanced Technical Design Document

## 1. Enhanced Project Scope & Objectives

### 1.1 Updated Primary Goals
- **Multi-Provider AI Support**: Support for 15+ AI providers including local and cloud services
- **Local-First Philosophy**: Prioritize privacy with local models while offering cloud options
- **Provider-Agnostic Design**: Seamless switching between different AI services
- **Advanced Configuration**: Provider-specific settings with intelligent defaults
- **Enterprise-Grade Reliability**: Fallback chains and provider health monitoring

### 1.2 Supported AI Providers (Cline/Roo Code Compatible)

**Local Providers** (Primary):
- **Ollama** - Local model hosting
- **LM Studio** - OpenAI-compatible local API
- **Jan.ai** - Privacy-focused local AI
- **GPT4All** - Local quantized models
- **Llamafile** - Self-contained model executables

**Cloud Providers** (Secondary):
- **OpenAI** - GPT-4o, GPT-4 Turbo, GPT-3.5
- **Anthropic** - Claude 3.5 Sonnet, Claude 3 Opus/Haiku
- **Google** - Gemini Pro, Gemini Flash
- **Microsoft** - Azure OpenAI Service
- **Mistral AI** - Mistral Large, Medium, Small
- **Cohere** - Command R+, Command R
- **Perplexity** - Llama 3.1 Sonar models
- **Groq** - Ultra-fast inference hosting
- **Together AI** - Open source model hosting
- **Fireworks AI** - High-performance model serving

## 2. Enhanced Architecture Design

### 2.1 Multi-Provider Plugin Structure
```
Brainarr.Plugin/
├── Brainarr.cs                           # Main plugin class
├── BrainarrImportList.cs                 # Core import list implementation
├── Configuration/
│   ├── BrainarrSettings.cs               # Main plugin settings
│   ├── ProviderSettings.cs               # Base provider settings
│   └── Providers/                        # Provider-specific configurations
│       ├── OllamaSettings.cs
│       ├── OpenAISettings.cs
│       ├── AnthropicSettings.cs
│       ├── GoogleSettings.cs
│       └── [...]Settings.cs
├── Services/
│   ├── Core/
│   │   ├── ILibraryAnalysisService.cs
│   │   ├── LibraryAnalysisService.cs
│   │   ├── ICacheService.cs
│   │   └── CacheService.cs
│   └── AI/
│       ├── IAIService.cs                 # Main AI service interface
│       ├── AIServiceFactory.cs           # Provider factory
│       ├── AIProviderManager.cs          # Multi-provider orchestration
│       └── Providers/                    # Individual provider implementations
│           ├── Local/
│           │   ├── OllamaProvider.cs
│           │   ├── LMStudioProvider.cs
│           │   ├── JanProvider.cs
│           │   └── GPT4AllProvider.cs
│           └── Cloud/
│               ├── OpenAIProvider.cs
│               ├── AnthropicProvider.cs
│               ├── GoogleProvider.cs
│               ├── MistralProvider.cs
│               └── [...]Provider.cs
├── Models/
│   ├── Core/
│   │   ├── LibraryProfile.cs
│   │   ├── AIRecommendation.cs
│   │   └── RecommendationBatch.cs
│   └── Providers/
│       ├── ProviderInfo.cs
│       ├── ModelInfo.cs
│       └── ProviderResponse.cs
├── Utilities/
│   ├── PromptBuilder.cs
│   ├── ResponseParser.cs
│   ├── ProviderDetector.cs              # Auto-detect available providers
│   └── ModelRecommendations.cs          # Suggest optimal models per provider
└── Resources/
    ├── PromptTemplates.resx
    ├── ProviderDocumentation.resx        # Provider setup guides
    └── ModelDescriptions.resx            # Model capability descriptions
```

### 2.2 Enhanced Data Flow
```
Library Analysis → Provider Selection → Failover Chain → AI Request → Response → Import Items
       ↓               ↓                    ↓              ↓           ↓           ↓
   Cache Check → Provider Health → Backup Provider → Parse → Validate → Cache Results
```

## 3. Multi-Provider AI Service Architecture

### 3.1 Core AI Service Interface
```csharp
public interface IAIProvider
{
    string Name { get; }
    ProviderType Type { get; }
    bool IsAvailable { get; }
    Task TestConnection();
    Task> GetAvailableModels();
    Task GenerateRecommendations(LibraryProfile profile, ProviderSettings settings);
    Task GetCapabilities();
}

public enum ProviderType
{
    Local,
    Cloud,
    Hybrid
}

public class ProviderCapabilities
{
    public int MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsSystemPrompts { get; set; }
    public List SupportedLanguages { get; set; }
    public double CostPerToken { get; set; }
    public TimeSpan TypicalResponseTime { get; set; }
}
```

### 3.2 Provider Manager with Failover
```csharp
public class AIProviderManager : IAIService
{
    private readonly List _providers;
    private readonly BrainarrSettings _settings;
    private readonly ILogger _logger;

    public async Task> GenerateRecommendations(LibraryProfile profile, BrainarrSettings settings)
    {
        var providerChain = BuildProviderChain(settings);
        
        foreach (var providerConfig in providerChain)
        {
            try
            {
                var provider = GetProvider(providerConfig.Name);
                if (!await provider.TestConnection())
                {
                    _logger.Debug($"Provider {provider.Name} unavailable, trying next");
                    continue;
                }

                var response = await provider.GenerateRecommendations(profile, providerConfig.Settings);
                var recommendations = ResponseParser.ParseRecommendations(response.Content);
                
                _logger.Info($"Successfully generated {recommendations.Count} recommendations using {provider.Name}");
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, $"Provider {providerConfig.Name} failed, trying next in chain");
            }
        }
        
        throw new InvalidOperationException("All configured AI providers failed");
    }

    private List BuildProviderChain(BrainarrSettings settings)
    {
        var chain = new List();
        
        // Primary provider
        chain.Add(new ProviderConfiguration 
        { 
            Name = settings.PrimaryProvider, 
            Settings = GetProviderSettings(settings.PrimaryProvider) 
        });
        
        // Fallback providers
        if (settings.EnableFallback)
        {
            foreach (var fallback in settings.FallbackProviders)
            {
                chain.Add(new ProviderConfiguration 
                { 
                    Name = fallback, 
                    Settings = GetProviderSettings(fallback) 
                });
            }
        }
        
        return chain;
    }
}
```

### 3.3 Local Provider Implementation (Ollama)
```csharp
public class OllamaProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    
    public string Name => "Ollama";
    public ProviderType Type => ProviderType.Local;
    
    public async Task> GetAvailableModels()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_settings.Endpoint}/api/tags");
            var result = await response.Content.ReadFromJsonAsync();
            return result.Models.Select(m => m.Name).ToList();
        }
        catch
        {
            return new List();
        }
    }

    public async Task GenerateRecommendations(LibraryProfile profile, ProviderSettings settings)
    {
        var ollamaSettings = (OllamaSettings)settings;
        var prompt = PromptBuilder.BuildRecommendationPrompt(profile, ollamaSettings);
        
        var request = new
        {
            model = ollamaSettings.ModelName,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = ollamaSettings.Temperature,
                top_p = ollamaSettings.TopP,
                num_predict = ollamaSettings.MaxTokens
            }
        };

        var response = await _httpClient.PostAsJsonAsync($"{ollamaSettings.Endpoint}/api/generate", request);
        var result = await response.Content.ReadFromJsonAsync();
        
        return new ProviderResponse
        {
            Content = result.Response,
            Provider = Name,
            Model = ollamaSettings.ModelName,
            TokensUsed = result.EvalCount ?? 0,
            ResponseTime = TimeSpan.FromMilliseconds(result.EvalDuration ?? 0)
        };
    }

    public async Task GetCapabilities()
    {
        return new ProviderCapabilities
        {
            MaxTokens = 32000, // Typical for Llama models
            SupportsStreaming = true,
            SupportsSystemPrompts = true,
            SupportedLanguages = new List { "en", "fr", "es", "de" },
            CostPerToken = 0.0, // Local is free
            TypicalResponseTime = TimeSpan.FromSeconds(5)
        };
    }
}
```

### 3.4 Cloud Provider Implementation (Anthropic Claude)
```csharp
public class AnthropicProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    
    public string Name => "Anthropic";
    public ProviderType Type => ProviderType.Cloud;

    public async Task GenerateRecommendations(LibraryProfile profile, ProviderSettings settings)
    {
        var anthropicSettings = (AnthropicSettings)settings;
        var prompt = PromptBuilder.BuildRecommendationPrompt(profile, anthropicSettings);
        
        var request = new
        {
            model = anthropicSettings.ModelName,
            max_tokens = anthropicSettings.MaxTokens,
            temperature = anthropicSettings.Temperature,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", anthropicSettings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.PostAsJsonAsync("https://api.anthropic.com/v1/messages", request);
        var result = await response.Content.ReadFromJsonAsync();
        
        return new ProviderResponse
        {
            Content = result.Content[0].Text,
            Provider = Name,
            Model = anthropicSettings.ModelName,
            TokensUsed = result.Usage.InputTokens + result.Usage.OutputTokens,
            ResponseTime = TimeSpan.FromSeconds(2) // Estimated
        };
    }

    public async Task GetCapabilities()
    {
        return new ProviderCapabilities
        {
            MaxTokens = 200000, // Claude 3.5 Sonnet
            SupportsStreaming = true,
            SupportsSystemPrompts = true,
            SupportedLanguages = new List { "en", "fr", "es", "de", "it", "pt", "ja", "ko", "zh" },
            CostPerToken = 0.000003, // $3 per million tokens for Claude 3.5 Sonnet
            TypicalResponseTime = TimeSpan.FromSeconds(3)
        };
    }
}
```

## 4. Enhanced Configuration System

### 4.1 Main Plugin Settings
```csharp
public class BrainarrSettings : IProviderConfig
{
    [FieldDefinition(0, Label = "Primary AI Provider", Type = FieldType.Select, SelectOptions = typeof(AIProviderType))]
    public AIProviderType PrimaryProvider { get; set; } = AIProviderType.Ollama;

    [FieldDefinition(1, Label = "Enable Fallback Providers", Type = FieldType.Checkbox, HelpText = "Try other providers if primary fails")]
    public bool EnableFallback { get; set; } = true;

    [FieldDefinition(2, Label = "Fallback Providers", Type = FieldType.MultiSelect, SelectOptions = typeof(AIProviderType), HelpText = "Providers to try if primary fails")]
    public List FallbackProviders { get; set; } = new() { AIProviderType.LMStudio };

    // Provider-specific settings
    [FieldDefinition(10, Label = "Ollama Settings", Type = FieldType.Subsection)]
    public OllamaSettings OllamaConfig { get; set; } = new();

    [FieldDefinition(11, Label = "OpenAI Settings", Type = FieldType.Subsection)]
    public OpenAISettings OpenAIConfig { get; set; } = new();

    [FieldDefinition(12, Label = "Anthropic Settings", Type = FieldType.Subsection)]
    public AnthropicSettings AnthropicConfig { get; set; } = new();

    [FieldDefinition(13, Label = "Google Settings", Type = FieldType.Subsection)]
    public GoogleSettings GoogleConfig { get; set; } = new();

    // General recommendation settings
    [FieldDefinition(20, Label = "Max Recommendations", Type = FieldType.Number, Min = 5, Max = 100)]
    public int MaxRecommendations { get; set; } = 20;

    [FieldDefinition(21, Label = "Minimum Confidence", Type = FieldType.Number, Min = 0.1, Max = 1.0)]
    public float MinimumConfidence { get; set; } = 0.6f;

    [FieldDefinition(22, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode))]
    public DiscoveryMode DiscoveryMode { get; set; } = DiscoveryMode.Balanced;
}
```

### 4.2 Provider-Specific Configuration
```csharp
public class OllamaSettings : ProviderSettings
{
    [FieldDefinition(0, Label = "Endpoint", HelpText = "Ollama server endpoint")]
    public string Endpoint { get; set; } = "http://localhost:11434";

    [FieldDefinition(1, Label = "Model Name", Type = FieldType.Select, DynamicOptions = true, HelpText = "Available models from Ollama")]
    public string ModelName { get; set; } = "llama3.2";

    [FieldDefinition(2, Label = "Temperature", Type = FieldType.Number, Min = 0.0, Max = 2.0)]
    public float Temperature { get; set; } = 0.7f;

    [FieldDefinition(3, Label = "Max Tokens", Type = FieldType.Number, Min = 100, Max = 32000)]
    public int MaxTokens { get; set; } = 2000;

    public override async Task> GetAvailableModels()
    {
        // Dynamic model fetching from Ollama API
        var provider = new OllamaProvider();
        return await provider.GetAvailableModels();
    }
}

public class AnthropicSettings : ProviderSettings
{
    [FieldDefinition(0, Label = "API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password)]
    public string ApiKey { get; set; } = "";

    [FieldDefinition(1, Label = "Model", Type = FieldType.Select, SelectOptions = typeof(AnthropicModel))]
    public AnthropicModel Model { get; set; } = AnthropicModel.Claude35Sonnet;

    [FieldDefinition(2, Label = "Max Tokens", Type = FieldType.Number, Min = 100, Max = 200000)]
    public int MaxTokens { get; set; } = 4000;

    [FieldDefinition(3, Label = "Temperature", Type = FieldType.Number, Min = 0.0, Max = 1.0)]
    public float Temperature { get; set; } = 0.7f;

    public string ModelName => Model switch
    {
        AnthropicModel.Claude35Sonnet => "claude-3-5-sonnet-20241022",
        AnthropicModel.Claude3Opus => "claude-3-opus-20240229",
        AnthropicModel.Claude3Haiku => "claude-3-haiku-20240307",
        _ => "claude-3-5-sonnet-20241022"
    };
}

public enum AnthropicModel
{
    Claude35Sonnet,
    Claude3Opus,
    Claude3Haiku
}
```

## 5. Provider Detection & User Guidance

### 5.1 Automatic Provider Detection
```csharp
public class ProviderDetector
{
    private readonly ILogger _logger;

    public async Task> DetectAvailableProviders()
    {
        var detected = new List();

        // Check local providers
        detected.AddRange(await CheckLocalProviders());
        
        // Check cloud providers (based on API keys)
        detected.AddRange(CheckCloudProviders());

        return detected;
    }

    private async Task> CheckLocalProviders()
    {
        var providers = new List();

        // Check Ollama
        if (await TestEndpoint("http://localhost:11434/api/tags"))
        {
            var models = await GetOllamaModels();
            providers.Add(new DetectedProvider
            {
                Name = "Ollama",
                Type = ProviderType.Local,
                IsAvailable = true,
                Endpoint = "http://localhost:11434",
                AvailableModels = models,
                RecommendedModel = GetBestOllamaModel(models),
                SetupInstructions = "Ollama detected and running. Recommended for privacy and no API costs."
            });
        }

        // Check LM Studio
        if (await TestEndpoint("http://localhost:1234/v1/models"))
        {
            providers.Add(new DetectedProvider
            {
                Name = "LM Studio",
                Type = ProviderType.Local,
                IsAvailable = true,
                Endpoint = "http://localhost:1234",
                SetupInstructions = "LM Studio detected. Load a model in LM Studio before using."
            });
        }

        return providers;
    }

    private string GetBestOllamaModel(List availableModels)
    {
        // Prioritize models based on capability and performance for music recommendations
        var preferences = new[] 
        { 
            "llama3.2:8b", "llama3.1:8b", "mistral:7b", "gemma2:9b",
            "llama3.2", "llama3.1", "mistral", "gemma2"
        };

        return preferences.FirstOrDefault(p => availableModels.Contains(p)) ?? availableModels.FirstOrDefault();
    }
}
```

### 5.2 User Guidance System
```csharp
public class ModelRecommendations
{
    public static ProviderRecommendation GetRecommendationFor(string scenario, List available)
    {
        return scenario.ToLower() switch
        {
            "privacy_focused" => new ProviderRecommendation
            {
                PrimaryProvider = "Ollama",
                RecommendedModel = "llama3.2:8b",
                Reasoning = "Local processing ensures complete privacy",
                SetupSteps = new[]
                {
                    "Install Ollama from https://ollama.ai",
                    "Run: ollama pull llama3.2:8b",
                    "Model will be ready for music recommendations"
                }
            },
            
            "best_quality" => new ProviderRecommendation
            {
                PrimaryProvider = "Anthropic",
                RecommendedModel = "Claude-3.5-Sonnet",
                Reasoning = "Excellent reasoning and cultural knowledge for music",
                SetupSteps = new[]
                {
                    "Get API key from https://console.anthropic.com",
                    "Add API key to Anthropic settings",
                    "Costs ~$0.01 per 100 recommendations"
                }
            },
            
            "balanced" => available.Any(p => p.Name == "Ollama") 
                ? GetRecommendationFor("privacy_focused", available)
                : GetRecommendationFor("best_quality", available),
                
            _ => GetRecommendationFor("balanced", available)
        };
    }
}
```

## 6. Enhanced User Interface & Documentation

### 6.1 Provider Setup Wizard
```csharp
public class ProviderSetupWizard
{
    public async Task GuideUserSetup(BrainarrSettings currentSettings)
    {
        var detectedProviders = await _providerDetector.DetectAvailableProviders();
        
        if (!detectedProviders.Any(p => p.IsAvailable))
        {
            return new SetupResult
            {
                Success = false,
                Message = "No AI providers detected. Please install Ollama or configure cloud provider.",
                NextSteps = new[]
                {
                    "For privacy: Install Ollama from https://ollama.ai",
                    "For best quality: Get Claude API key from https://console.anthropic.com",
                    "For free cloud option: Get OpenAI API key (has usage costs)"
                }
            };
        }

        var bestLocal = detectedProviders.FirstOrDefault(p => p.Type == ProviderType.Local && p.IsAvailable);
        if (bestLocal != null)
        {
            return new SetupResult
            {
                Success = true,
                RecommendedSettings = new BrainarrSettings
                {
                    PrimaryProvider = Enum.Parse(bestLocal.Name),
                    EnableFallback = false
                },
                Message = $"Found {bestLocal.Name} running locally. This is the recommended setup for privacy.",
                NextSteps = new[] { $"Recommended model: {bestLocal.RecommendedModel}" }
            };
        }

        return GuideCloudSetup();
    }
}
```

### 6.2 Interactive Documentation
```csharp
public static class ProviderDocumentation
{
    public static string GetSetupGuide(AIProviderType provider)
    {
        return provider switch
        {
            AIProviderType.Ollama => @"
# Ollama Setup (Recommended for Privacy)

## Installation
1. Download from: https://ollama.ai
2. Install and start Ollama
3. Pull a recommended model:
   ```
   ollama pull llama3.2:8b
   ```

## Configuration
- Endpoint: http://localhost:11434 (default)
- Recommended models for music:
  - llama3.2:8b (8GB RAM) - Best balance
  - mistral:7b (7GB RAM) - Faster
  - gemma2:9b (9GB RAM) - Better reasoning

## Pros
✅ Complete privacy - data never leaves your machine
✅ No API costs
✅ Works offline
✅ Fast responses once loaded

## Cons
❌ Requires 8GB+ RAM
❌ Initial model download (~5GB)
❌ May need GPU for best performance",

            AIProviderType.Anthropic => @"
# Anthropic Claude Setup (Best Quality)

## Getting API Key
1. Visit: https://console.anthropic.com
2. Create account and verify phone
3. Go to API Keys section
4. Create new key with appropriate limits

## Configuration
- API Key: [Your key from console]
- Recommended model: Claude-3.5-Sonnet
- Max tokens: 4000 (good for ~20 recommendations)

## Pricing (as of 2024)
- Claude-3.5-Sonnet: $3/million input tokens, $15/million output
- Typical cost: ~$0.01 per 100 music recommendations
- Monthly limit: Usually $100-500 depending on account

## Pros
✅ Excellent cultural and music knowledge
✅ Very reliable JSON output
✅ Fast response times
✅ High context window (200k tokens)

## Cons
❌ Requires internet
❌ Has usage costs
❌ Rate limits on free tier",

            _ => "Documentation not available for this provider."
        };
    }
}
```