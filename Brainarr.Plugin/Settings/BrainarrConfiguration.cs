using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Settings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Brainarr.Settings
{
    /// <summary>
    /// Core configuration class for Brainarr settings.
    /// Handles the basic configuration properties without UI or validation concerns.
    /// </summary>
    public class BrainarrConfiguration : SecureSettingsBase, IImportListSettings
    {
        // Core Settings
        public virtual NzbDroneValidationResult Validate() => new NzbDroneValidationResult();
        
        // Provider Selection
        public AIProvider Provider { get; set; } = AIProvider.Ollama;
        
        // Recommendation Settings
        public int MaxRecommendations { get; set; } = BrainarrConstants.DefaultRecommendations;
        public RecommendationMode RecommendationMode { get; set; } = RecommendationMode.Albums;
        public bool EnableIterativeRefinement { get; set; } = true;
        public int MaxIterations { get; set; } = 3;
        
        // Library Analysis Settings
        public bool EnableLibraryAnalysis { get; set; } = true;
        public int LibrarySampleSize { get; set; } = 50;
        public bool AnalyzeListeningHistory { get; set; } = true;
        
        // Caching Settings
        public bool EnableCaching { get; set; } = true;
        public int CacheExpirationHours { get; set; } = 24;
        public int MaxCacheSize { get; set; } = 1000;
        
        // Advanced Settings
        public bool EnableHallucinationDetection { get; set; } = true;
        public double HallucinationThreshold { get; set; } = 0.7;
        public bool EnableDuplicateDetection { get; set; } = true;
        public bool EnableMusicBrainzValidation { get; set; } = false;
        public bool EnableProviderFallback { get; set; } = true;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxRetryAttempts { get; set; } = 3;
        
        // Model Selection (per provider)
        public string OllamaModel { get; set; } = "llama2";
        public string LMStudioModel { get; set; } = "local-model";
        public string OpenAIModel { get; set; } = "gpt-4";
        public string AnthropicModel { get; set; } = "claude-3-opus-20240229";
        public string GeminiModel { get; set; } = "gemini-pro";
        public string GroqModel { get; set; } = "mixtral-8x7b-32768";
        public string PerplexityModel { get; set; } = "mixtral-8x7b-instruct";
        public string DeepSeekModel { get; set; } = "deepseek-chat";
        public string OpenRouterModel { get; set; } = "anthropic/claude-3-opus";
        
        // Provider URLs
        private string _ollamaUrl = BrainarrConstants.DefaultOllamaUrl;
        private string _lmStudioUrl = BrainarrConstants.DefaultLMStudioUrl;
        
        public string OllamaUrl
        {
            get => _ollamaUrl;
            set => _ollamaUrl = NormalizeUrl(value) ?? BrainarrConstants.DefaultOllamaUrl;
        }
        
        public string LMStudioUrl
        {
            get => _lmStudioUrl;
            set => _lmStudioUrl = NormalizeUrl(value) ?? BrainarrConstants.DefaultLMStudioUrl;
        }
        
        // API Key Management - Secure Storage
        public string OpenAIApiKey
        {
            get => GetApiKeySecurely(AIProvider.OpenAI.ToString());
            set => StoreApiKeySecurely(AIProvider.OpenAI.ToString(), value);
        }
        
        public string AnthropicApiKey
        {
            get => GetApiKeySecurely(AIProvider.Anthropic.ToString());
            set => StoreApiKeySecurely(AIProvider.Anthropic.ToString(), value);
        }
        
        public string GeminiApiKey
        {
            get => GetApiKeySecurely(AIProvider.Gemini.ToString());
            set => StoreApiKeySecurely(AIProvider.Gemini.ToString(), value);
        }
        
        public string GroqApiKey
        {
            get => GetApiKeySecurely(AIProvider.Groq.ToString());
            set => StoreApiKeySecurely(AIProvider.Groq.ToString(), value);
        }
        
        public string PerplexityApiKey
        {
            get => GetApiKeySecurely(AIProvider.Perplexity.ToString());
            set => StoreApiKeySecurely(AIProvider.Perplexity.ToString(), value);
        }
        
        public string DeepSeekApiKey
        {
            get => GetApiKeySecurely(AIProvider.DeepSeek.ToString());
            set => StoreApiKeySecurely(AIProvider.DeepSeek.ToString(), value);
        }
        
        public string OpenRouterApiKey
        {
            get => GetApiKeySecurely(AIProvider.OpenRouter.ToString());
            set => StoreApiKeySecurely(AIProvider.OpenRouter.ToString(), value);
        }
        
        // Raw URL properties for UI binding (used by field definitions)
        public string OllamaUrlRaw { get; set; }
        public string LMStudioUrlRaw { get; set; }
        
        // Utility Methods
        protected string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            
            url = url.Trim();
            
            // Ensure URL has protocol
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "http://" + url;
            }
            
            // Remove trailing slash
            return url.TrimEnd('/');
        }
        
        /// <summary>
        /// Gets the appropriate API key for the current provider
        /// </summary>
        public string GetCurrentProviderApiKey()
        {
            return Provider switch
            {
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                _ => null
            };
        }
        
        /// <summary>
        /// Gets the appropriate model for the current provider
        /// </summary>
        public string GetCurrentProviderModel()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaModel,
                AIProvider.LMStudio => LMStudioModel,
                AIProvider.OpenAI => OpenAIModel,
                AIProvider.Anthropic => AnthropicModel,
                AIProvider.Gemini => GeminiModel,
                AIProvider.Groq => GroqModel,
                AIProvider.Perplexity => PerplexityModel,
                AIProvider.DeepSeek => DeepSeekModel,
                AIProvider.OpenRouter => OpenRouterModel,
                _ => null
            };
        }
        
        /// <summary>
        /// Gets the appropriate URL for local providers
        /// </summary>
        public string GetCurrentProviderUrl()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaUrl,
                AIProvider.LMStudio => LMStudioUrl,
                _ => null
            };
        }
    }
}