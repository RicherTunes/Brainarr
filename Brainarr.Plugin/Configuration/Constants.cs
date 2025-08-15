namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class BrainarrConstants
    {
        // Default URLs - Using localhost for better deployment flexibility
        // Users can override these in the UI settings
        public const string DefaultOllamaUrl = "http://localhost:11434";
        public const string DefaultLMStudioUrl = "http://localhost:1234";
        
        // API Endpoints (moved from hardcoded in providers)
        public const string OpenAIApiUrl = "https://api.openai.com/v1/chat/completions";
        public const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
        public const string AnthropicVersion = "2023-06-01";
        public const string PerplexityApiUrl = "https://api.perplexity.ai/chat/completions";
        public const string DeepSeekApiUrl = "https://api.deepseek.com/v1/chat/completions";
        public const string GeminiApiUrlFormat = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
        public const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";
        public const string OpenRouterApiUrl = "https://openrouter.ai/api/v1/chat/completions";
        
        // Default models
        public const string DefaultOllamaModel = "qwen2.5:latest";
        public const string DefaultLMStudioModel = "local-model";
        public const string DefaultOpenAIModel = "gpt-4o-mini";
        public const string DefaultAnthropicModel = "claude-3-5-haiku-latest";
        public const string DefaultPerplexityModel = "llama-3.1-sonar-large-128k-online";
        public const string DefaultDeepSeekModel = "deepseek-chat";
        public const string DefaultGeminiModel = "gemini-1.5-flash";
        public const string DefaultGroqModel = "llama-3.3-70b-versatile";
        public const string DefaultOpenRouterModel = "anthropic/claude-3.5-haiku";
        
        // Limits
        public const int MinRecommendations = 1;
        public const int MaxRecommendations = 50;
        public const int DefaultRecommendations = 20;
        
        // Token limits
        public const int MaxTokensDefault = 2000;
        public const int MaxTokensLarge = 4096;
        public const int MaxTokensTest = 10;
        
        // Temperature settings
        public const double DefaultTemperature = 0.8;
        public const double CreativeTemperature = 0.9;
        public const double PreciseTemperature = 0.7;
        
        // Timeouts (in seconds)
        public const int DefaultAITimeout = 30;
        public const int MaxAITimeout = 120;
        public const int ModelDetectionTimeout = 10;
        public const int TestConnectionTimeout = 10;
        
        // Retry Policy
        public const int MaxRetryAttempts = 3;
        public const int InitialRetryDelayMs = 1000;
        public const int MaxRetryDelayMs = 30000;
        
        // Rate Limiting (per provider)
        public const int RequestsPerMinute = 10;
        public const int BurstSize = 5;
        
        // Health Monitoring
        public const int HealthCheckTimeoutMs = 5000;
        public const double UnhealthyThreshold = 0.5; // 50% failure rate
        public const int HealthCheckWindowMinutes = 5;
        public const double SlowResponseThresholdMs = 5000;
        
        // Caching
        public const int CacheDurationMinutes = 60;
        public const int MaxCacheEntries = 100;
        
        // Confidence thresholds
        public const double HighConfidenceThreshold = 0.7;
        public const double MediumConfidenceThreshold = 0.5;
        public const double DefaultConfidence = 0.8;
        
        // Common genres (fallback when real data unavailable)
        public static readonly string[] FallbackGenres = new[]
        {
            "Rock", "Electronic", "Pop", "Jazz", "Classical", 
            "Hip Hop", "R&B", "Country", "Folk", "Metal"
        };
    }
}