namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class BrainarrConstants
    {
        // ── Plugin identity (Wave-23: ecosystem parity with apple/tidal/qobuz) ──
        // The triple matches the AppleMusicarrConstants / TidalConstants / QobuzarrConstants
        // pattern. Currently BrainarrInstalledPlugin still hardcodes these as literals (the
        // host-registration class is the load-bearing site); these consts provide a single
        // source of truth for any new code site that needs to reference the plugin identity.
        public const string PluginName = "Brainarr";
        public const string ServiceName = "brainarr";
        public const string PluginVendor = "RicherTunes";

        // Default URLs - Using localhost for better deployment flexibility
        // Users can override these in the UI settings
        public const string DefaultOllamaUrl = "http://localhost:11434";
        public const string DefaultLMStudioUrl = "http://localhost:1234";

        // Default models (local)
        public const string DefaultOllamaModel = "qwen2.5:latest";
        public const string DefaultLMStudioModel = "local-model";

        // Default models (cloud/gateways) — UI labels expected by tests.
        // May 2026: bumped to current-generation models. Existing user settings
        // still resolve via back-compat enum entries + ModelIdMapper.
        public const string DefaultOpenAIModel = "GPT5_Mini";
        public const string DefaultPerplexityModel = "Sonar_Pro";
        public const string DefaultAnthropicModel = "ClaudeSonnet46";
        public const string DefaultOpenRouterModel = "Auto";
        public const string DefaultDeepSeekModel = "DeepSeek_V4_Flash";
        public const string DefaultGeminiModel = "Gemini_3_Flash";
        public const string DefaultGroqModel = "Llama33_70B_Versatile";
        // Z.AI GLM default. GLM-4.5-Air is the cost/quality sweet spot for the
        // prompt sizes brainarr sends; flagship GLM-5.1 is more expensive and
        // sized for long-horizon agentic tasks brainarr doesn't need.
        public const string DefaultZaiGlmModel = "GLM_4_5_Air";
        // Z.AI Coding Plan default. Coding-Plan subscribers paid for premium model
        // access — default to the flagship GLM-5.1. Basic-tier subscribers whose
        // package doesn't include GLM-5.1 will get a clear QuotaExceeded hint
        // (see BrainarrZaiCodingProvider.GetUserHint) prompting them to switch
        // to GLM-4.5-Air, the entry-point model on every tier.
        public const string DefaultZaiCodingModel = "GLM_5_1";

        // Default models (subscription-based providers)
        public const string DefaultClaudeCodeModel = "claude-sonnet-4-5-20250514";
        public const string DefaultOpenAICodexModel = "gpt-4o";

        // OpenRouter: lightweight test model
        public const string DefaultOpenRouterTestModelRaw = "gpt-4.1-mini";

        // Limits
        public const int MinRecommendations = 1;
        public const int MaxRecommendations = 50;
        public const int DefaultRecommendations = 20;

        // Timeouts (in seconds)
        public const int MinAITimeout = 5;
        public const int DefaultAITimeout = 30;
        public const int MaxAITimeout = 600; // 10 minutes max for slow local models
        public const int LocalProviderDefaultTimeout = 360; // 6 minutes for local LLMs
        public const int ModelDetectionTimeout = 10;
        public const int ModelDetectionCacheMinutes = 10;
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

        // Backend health cache: how long a known-dead local backend (connection refused)
        // is short-circuited before retrying. 30 s is long enough to absorb the Lidarr
        // UI "Test → form reload" cycle (typically < 5 s) while being short enough that
        // a user who actually starts their backend gets a live check within half a minute.
        public const int BackendDownGraceSeconds = 30;

        // Caching
        public const int CacheDurationMinutes = 60;
        public const int MaxCacheEntries = 100;
        public const int MaxMemoryCacheEntries = 1000;

        // Async Operations
        public const int DefaultAsyncTimeoutMs = 120000; // 2 minutes

        // Token Limits (default for provider responses)
        public const int DefaultMaxTokens = 2000;
        public const int CloudProviderMaxTokens = 1500;
        public const int LocalProviderMaxTokens = 4096;

        // Circuit Breaker
        public const int CircuitBreakerSamplingWindow = 20;
        public const int CircuitBreakerMinimumThroughput = 10;
        public const double CircuitBreakerFailureThreshold = 0.5; // 50%
        public const int CircuitBreakerDurationSeconds = 30;

        // Input Validation
        public const int MaxGenreNameLength = 100;
        public const int MaxPromptLength = 5000;
        public const int RegexTimeoutMs = 100;

        // Import List Settings
        public const int MinRefreshIntervalHours = 6;

        // Common genres (fallback when real data unavailable)
        public static readonly string[] FallbackGenres = new[]
        {
            "Rock", "Electronic", "Pop", "Jazz", "Classical",
            "Hip Hop", "R&B", "Country", "Folk", "Metal"
        };

        // Styles Catalog (dynamic JSON)
        // Canonical GitHub raw URL for the maintained catalog. Optional remote override; embedded catalog remains authoritative.
        public const string StylesCatalogUrl = "https://raw.githubusercontent.com/RicherTunes/Brainarr/main/Brainarr.Plugin/Resources/music_styles.json";
        public const int StylesCatalogRefreshHours = 24; // periodic auto-refresh
        public const int StylesCatalogTimeoutMs = 5000;  // ms network timeout for catalog fetch

        // Provider API endpoints
        public const string OpenAIChatCompletionsUrl = "https://api.openai.com/v1/chat/completions";
        public const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
        public const string GroqChatCompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";
        public const string DeepSeekChatCompletionsUrl = "https://api.deepseek.com/chat/completions";
        // Z.AI international endpoint. Zhipu also exposes a Chinese-domain mirror at
        // open.bigmodel.cn but api.z.ai is the documented international path and
        // avoids GFW-related connectivity issues for non-CN users.
        public const string ZaiGlmChatCompletionsUrl = "https://api.z.ai/api/paas/v4/chat/completions";
        // Z.AI Coding Plan endpoint — Anthropic Messages API compatible. Used by
        // Claude Code, Cline, and OpenCode when ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic.
        // Same host as the PaaS endpoint above, but Coding Plan subscriptions are gated
        // to this path and served the GLM-5.x family. Reference:
        // docs.z.ai/scenario-example/develop-tools/claude.
        public const string ZaiCodingMessagesUrl = "https://api.z.ai/api/anthropic/v1/messages";
        public const string AnthropicMessagesUrl = "https://api.anthropic.com/v1/messages";
        public const string GeminiModelsBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

        // OpenRouter headers context
        public const string ProjectReferer = "https://github.com/RicherTunes/Brainarr";
        public const string OpenRouterTitle = "Brainarr";

        // Behavior/versioning
        // Increment when sanitizer or schema behavior changes in a way that should invalidate caches
        public const int SanitizerVersion = 1;
        // Bump when cache key composition changes
        public const int CacheKeyVersion = 2;

        // Documentation links (GitHub docs)
        public const string DocsBaseUrl = "https://github.com/RicherTunes/Brainarr/blob/main/docs";
        public const string DocsTroubleshootingUrl = DocsBaseUrl + "/troubleshooting.md";
        public const string DocsProviderGuideUrl = DocsBaseUrl + "/PROVIDER_GUIDE.md";
        public const string DocsUserSetupGuideUrl = DocsBaseUrl + "/USER_SETUP_GUIDE.md";

        // Specific anchors
        public const string DocsGeminiServiceDisabled = DocsTroubleshootingUrl + "#403-permission_denied-service_disabled";
        public const string DocsGeminiSection = DocsTroubleshootingUrl + "#google-gemini";
        public const string DocsOpenAIInvalidKey = DocsTroubleshootingUrl + "#invalid-api-key";
        public const string DocsOpenAIRateLimit = DocsTroubleshootingUrl + "#rate-limit-exceeded";
        public const string DocsAnthropicCreditLimit = DocsTroubleshootingUrl + "#credit-limit-reached";
        public const string DocsAnthropicSection = DocsTroubleshootingUrl + "#anthropic";
        public const string DocsOpenRouterSection = DocsTroubleshootingUrl + "#openrouter";
        public const string DocsGroqSection = DocsTroubleshootingUrl + "#groq";
        public const string DocsDeepSeekSection = DocsTroubleshootingUrl + "#deepseek";
        public const string DocsZaiGlmSection = DocsTroubleshootingUrl + "#zai-glm";
        public const string DocsZaiCodingSection = DocsTroubleshootingUrl + "#zai-coding";
        public const string DocsPerplexitySection = DocsTroubleshootingUrl + "#perplexity";
        public const string DocsOllamaSection = DocsTroubleshootingUrl + "#ollama";
        public const string DocsLMStudioSection = DocsTroubleshootingUrl + "#lm-studio";
    }
}
