using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public partial class BrainarrSettings
    {
        [FieldDefinition(3, Label = "API Key", Type = FieldType.Password, Privacy = PrivacyLevel.Password,
            HelpText = "Enter your API key for the selected provider. Not needed for local providers (Ollama/LM Studio)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#api-keys")]
        public string ApiKey
        {
            get => Provider switch
            {
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                _ => null
            };
            set
            {
                switch (Provider)
                {
                    case AIProvider.Perplexity:
                        PerplexityApiKey = value;
                        break;
                    case AIProvider.OpenAI:
                        OpenAIApiKey = value;
                        break;
                    case AIProvider.Anthropic:
                        AnthropicApiKey = value;
                        break;
                    case AIProvider.OpenRouter:
                        OpenRouterApiKey = value;
                        break;
                    case AIProvider.DeepSeek:
                        DeepSeekApiKey = value;
                        break;
                    case AIProvider.Gemini:
                        GeminiApiKey = value;
                        break;
                    case AIProvider.Groq:
                        GroqApiKey = value;
                        break;
                }
            }
        }

        // Hidden backing fields for all providers
        public string OllamaUrl
        {
            get => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl;
            set => _ollamaUrl = NormalizeHttpUrlOrOriginal(value);
        }

        // Internal property for validation - returns actual value without defaults
        internal string OllamaUrlRaw => _ollamaUrl;

        public string OllamaModel
        {
            get => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel;
            set => _ollamaModel = value;
        }

        public string LMStudioUrl
        {
            get => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl;
            set => _lmStudioUrl = NormalizeHttpUrlOrOriginal(value);
        }

        // Internal property for validation - returns actual value without defaults
        internal string LMStudioUrlRaw => _lmStudioUrl;

        public string LMStudioModel
        {
            get => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel;
            set => _lmStudioModel = value;
        }

        // Hidden backing properties for all API-based providers
        // SECURITY: API keys are stored as strings and only marked as Password in UI fields.
        // Do not log these values; consider external secret storage if needed.
        private string? _perplexityApiKey;
        private string? _openAIApiKey;
        private string? _anthropicApiKey;
        private string? _openRouterApiKey;
        private string? _deepSeekApiKey;
        private string? _geminiApiKey;
        private string? _groqApiKey;

        public string PerplexityApiKey
        {
            get => _perplexityApiKey;
            set => _perplexityApiKey = SanitizeApiKey(value);
        }
        // New canonical model id properties per provider
        public string? PerplexityModelId { get; set; }
        // Backward-compat aliases for tests and legacy code
        public string? PerplexityModel { get => PerplexityModelId; set => PerplexityModelId = ProviderModelNormalizer.Normalize(AIProvider.Perplexity, value); }
        public string? OpenAIApiKey
        {
            get => _openAIApiKey;
            set => _openAIApiKey = SanitizeApiKey(value);
        }
        public string? OpenAIModelId { get; set; }
        public string? OpenAIModel { get => OpenAIModelId; set => OpenAIModelId = value; }
        public string? AnthropicApiKey
        {
            get => _anthropicApiKey;
            set => _anthropicApiKey = SanitizeApiKey(value);
        }
        public string? AnthropicModelId { get; set; }
        public string? AnthropicModel { get => AnthropicModelId; set => AnthropicModelId = value; }
        public string? OpenRouterApiKey
        {
            get => _openRouterApiKey;
            set => _openRouterApiKey = SanitizeApiKey(value);
        }
        public string? OpenRouterModelId { get; set; }
        public string? OpenRouterModel { get => OpenRouterModelId; set => OpenRouterModelId = value; }
        public string? DeepSeekApiKey
        {
            get => _deepSeekApiKey;
            set => _deepSeekApiKey = SanitizeApiKey(value);
        }
        public string? DeepSeekModelId { get; set; }
        public string? DeepSeekModel { get => DeepSeekModelId; set => DeepSeekModelId = value; }
        public string? GeminiApiKey
        {
            get => _geminiApiKey;
            set => _geminiApiKey = SanitizeApiKey(value);
        }
        public string? GeminiModelId { get; set; }
        public string? GeminiModel { get => GeminiModelId; set => GeminiModelId = value; }
        public string? GroqApiKey
        {
            get => _groqApiKey;
            set => _groqApiKey = SanitizeApiKey(value);
        }
        public string? GroqModelId { get; set; }
        public string? GroqModel { get => GroqModelId; set => GroqModelId = value; }

        // ===== Subscription-based Providers (Claude Code / OpenAI Codex) =====
        // These use credential files instead of API keys

        private string? _claudeCodeCredentialsPath;
        private string? _openAICodexCredentialsPath;

        /// <summary>
        /// Path to Claude Code credentials file (~/.claude/.credentials.json by default).
        /// Run 'claude login' to generate credentials.
        /// </summary>
        [FieldDefinition(38, Label = "Claude Code Credentials Path", Type = FieldType.Path,
            HelpText = "Path to your Claude Code credentials file. Default: ~/.claude/.credentials.json\nRun 'claude login' in terminal to authenticate. The Test button will validate your credentials.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#claude-code-subscription")]
        public string ClaudeCodeCredentialsPath
        {
            get => string.IsNullOrEmpty(_claudeCodeCredentialsPath)
                ? Services.SubscriptionCredentialLoader.GetDefaultClaudeCodePath()
                : _claudeCodeCredentialsPath;
            set => _claudeCodeCredentialsPath = value;
        }

        public string? ClaudeCodeModelId { get; set; }

        /// <summary>
        /// Path to OpenAI Codex auth file (~/.codex/auth.json by default).
        /// Run 'codex auth login' to generate credentials.
        /// </summary>
        [FieldDefinition(39, Label = "OpenAI Codex Credentials Path", Type = FieldType.Path,
            HelpText = "Path to your OpenAI Codex auth file. Default: ~/.codex/auth.json\nRun 'codex auth login' in terminal to authenticate. The Test button will validate your credentials.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#openai-codex-subscription")]
        public string OpenAICodexCredentialsPath
        {
            get => string.IsNullOrEmpty(_openAICodexCredentialsPath)
                ? Services.SubscriptionCredentialLoader.GetDefaultCodexPath()
                : _openAICodexCredentialsPath;
            set => _openAICodexCredentialsPath = value;
        }

        public string? OpenAICodexModelId { get; set; }

        // No backward-compat properties; canonical fields are *ModelId
    }
}
