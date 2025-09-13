namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class BrainarrConstants
    {
        public const string DefaultOllamaUrl = "http://localhost:11434";
        public const string DefaultLMStudioUrl = "http://localhost:1234";

        public const string DefaultOllamaModel = "qwen2.5:latest";
        public const string DefaultLMStudioModel = "local-model";

        public const string DefaultOpenAIModel = "GPT4o_Mini";
        public const string DefaultPerplexityModel = "Sonar_Large";
        public const string DefaultAnthropicModel = "Claude35_Haiku";
        public const string DefaultOpenRouterModel = "Claude35_Haiku";
        public const string DefaultDeepSeekModel = "DeepSeek_Chat";
        public const string DefaultGeminiModel = "Gemini_15_Flash";
        public const string DefaultGroqModel = "Llama33_70B";

        public const string StylesCatalogUrl = "https://raw.githubusercontent.com/RicherTunes/Brainarr/main/catalog/music_styles.json";
        public const int StylesCatalogRefreshHours = 24;
        public const int StylesCatalogTimeoutMs = 8000;

        public const string DefaultOpenRouterTestModelRaw = "gpt-4o-mini";

        public const int MinRecommendations = 1;
        public const int MaxRecommendations = 50;
        public const int DefaultRecommendations = 20;

        public const int DefaultAITimeout = 30;
        public const int MaxAITimeout = 120;
        public const int ModelDetectionTimeout = 10;
        public const int ModelDetectionCacheMinutes = 10;
        public const int TestConnectionTimeout = 10;

        public const int MaxRetryAttempts = 3;
        public const int InitialRetryDelayMs = 1000;
        public const int MaxRetryDelayMs = 30000;

        public const int RequestsPerMinute = 10;
        public const int BurstSize = 5;

        public const int HealthCheckTimeoutMs = 5000;
        public const double UnhealthyThreshold = 0.5;
        public const int HealthCheckWindowMinutes = 5;

        public const int CacheDurationMinutes = 60;
        public const int MaxCacheEntries = 100;

        public const int DefaultAsyncTimeoutMs = 120000;

        public const int MinRefreshIntervalHours = 6;

        public static readonly string[] FallbackGenres = new[]
        {
            "Rock", "Electronic", "Pop", "Jazz", "Classical",
            "Hip Hop", "R&B", "Country", "Folk", "Metal"
        };

        public const string OpenAIChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";
        public const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        public const string GroqChatCompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";
        public const string DeepSeekChatCompletionsUrl = "https://api.deepseek.com/chat/completions";
        public const string AnthropicMessagesUrl = "https://api.anthropic.com/v1/messages";
        public const string GeminiModelsBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

        public const string ProjectReferer = "https://github.com/RicherTunes/Brainarr";
        public const string OpenRouterTitle = "Brainarr";

        public const int SanitizerVersion = 1;
        public const int CacheKeyVersion = 2;
    }
}

