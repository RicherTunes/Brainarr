using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using System.Text.Json.Serialization;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    // Validator and enums moved to Configuration/ for clarity


    public partial class BrainarrSettings : IImportListSettings
    {
        private static readonly BrainarrSettingsValidator Validator = new BrainarrSettingsValidator();
        private AIProvider _provider;
        private AIProvider? _previousProvider;

        public BrainarrSettings()
        {
            // Sensible defaults that actually work
            _provider = AIProvider.Ollama;
            _ollamaUrl = BrainarrConstants.DefaultOllamaUrl;
            _ollamaModel = BrainarrConstants.DefaultOllamaModel;
            _lmStudioUrl = BrainarrConstants.DefaultLMStudioUrl;
            _lmStudioModel = BrainarrConstants.DefaultLMStudioModel;
            MaxRecommendations = BrainarrConstants.DefaultRecommendations;
            DiscoveryMode = DiscoveryMode.Adjacent;
            SamplingStrategy = SamplingStrategy.Balanced;
            RecommendationMode = RecommendationMode.SpecificAlbums;
            AutoDetectModel = true;
            // Default iterative refinement on for local default provider
            EnableIterativeRefinement = true;
            BackfillStrategy = BackfillStrategy.Standard;
        }

        // ====== QUICK START GUIDE ======
        [FieldDefinition(0, Label = "AI Provider", Type = FieldType.Select, SelectOptions = typeof(AIProvider),
            HelpText = "Choose your AI provider:\n- LOCAL (Private): Ollama, LM Studio — Your data stays private\n- GATEWAY: OpenRouter — Access 200+ models with one key\n- BUDGET: DeepSeek, Gemini — Low cost or free\n- FAST: Groq — Ultra-fast responses\n- PREMIUM: OpenAI, Anthropic — Best quality\n\nNote: After selecting, click 'Test' to verify connection!", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#choosing-a-provider")]
        public AIProvider Provider
        {
            get => _provider;
            set
            {
                if (_provider != value)
                {
                    _previousProvider = _provider;
                    _provider = value;
                    // Don't clear any models - preserve settings for each provider
                    // Auto-enable iterative refinement for local providers for better fill behavior
                    if (_provider == AIProvider.Ollama || _provider == AIProvider.LMStudio)
                    {
                        EnableIterativeRefinement = true;
                    }
                    // Ensure a stable default model when switching to providers with ambiguous defaults
                    if (_provider == AIProvider.OpenRouter && string.IsNullOrWhiteSpace(OpenRouterModelId))
                    {
                        OpenRouterModelId = BrainarrConstants.DefaultOpenRouterModel;
                    }
                }
                else
                {
                    // Same provider - treat as reset operation
                    ClearCurrentProviderModel();
                }
            }
        }

        // Ollama Settings
        private string _ollamaUrl;
        private string _ollamaModel;
        private string _lmStudioUrl;
        private string _lmStudioModel;

        [FieldDefinition(1, Label = "Configuration URL", Type = FieldType.Textbox,
            HelpText = "Only used for local providers (Ollama/LM Studio). For cloud/API-key providers (OpenAI, Anthropic, Perplexity, OpenRouter, DeepSeek, Gemini, Groq) this shows 'N/A' and is ignored.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Provider-Basics#configuration-url")]
        public string ConfigurationUrl
        {
            get => Provider switch
            {
                AIProvider.Ollama => string.IsNullOrEmpty(_ollamaUrl) ? BrainarrConstants.DefaultOllamaUrl : _ollamaUrl,
                AIProvider.LMStudio => string.IsNullOrEmpty(_lmStudioUrl) ? BrainarrConstants.DefaultLMStudioUrl : _lmStudioUrl,
                _ => "N/A - API Key based provider"
            };
            set
            {
                if (Provider == AIProvider.Ollama) _ollamaUrl = value;
                else if (Provider == AIProvider.LMStudio) _lmStudioUrl = value;
            }
        }

        [FieldDefinition(2, Label = "Model Selection", Type = FieldType.Select, SelectOptionsProviderAction = "getModelOptions",
            HelpText = "IMPORTANT: Click 'Test' first to auto-detect available models!", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#model-selection")]
        public string ModelSelection
        {
            get
            {
                return Provider switch
                {
                    AIProvider.Ollama => string.IsNullOrEmpty(_ollamaModel) ? BrainarrConstants.DefaultOllamaModel : _ollamaModel,
                    AIProvider.LMStudio => string.IsNullOrEmpty(_lmStudioModel) ? BrainarrConstants.DefaultLMStudioModel : _lmStudioModel,
                    AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                    AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                    AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                    AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                    AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                    AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                    AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                    AIProvider.ClaudeCodeSubscription => string.IsNullOrEmpty(ClaudeCodeModelId) ? BrainarrConstants.DefaultClaudeCodeModel : ClaudeCodeModelId,
                    AIProvider.OpenAICodexSubscription => string.IsNullOrEmpty(OpenAICodexModelId) ? BrainarrConstants.DefaultOpenAICodexModel : OpenAICodexModelId,
                    _ => "Default"
                };
            }
            set
            {
                // Guard against stale UI value being applied to a newly-switched provider.
                // Example: switched from Perplexity -> LM Studio, but UI still posts "sonar-large".
                if (Provider == AIProvider.LMStudio && IsPerplexityModelValue(value))
                {
                    // Treat as selection for Perplexity (previous provider) and ignore for LM Studio
                    PerplexityModelId = ProviderModelNormalizer.Normalize(AIProvider.Perplexity, value);
                    return;
                }
                if (Provider == AIProvider.Perplexity && LooksLikeLocalModelValue(value))
                {
                    // Treat as selection for LM Studio/Ollama depending on previous provider
                    if (_previousProvider == AIProvider.Ollama)
                    {
                        _ollamaModel = value;
                    }
                    else
                    {
                        _lmStudioModel = value;
                    }
                    return;
                }

                switch (Provider)
                {
                    case AIProvider.Ollama: _ollamaModel = value; break;
                    case AIProvider.LMStudio: _lmStudioModel = value; break;
                    case AIProvider.Perplexity: PerplexityModelId = ProviderModelNormalizer.Normalize(AIProvider.Perplexity, value); break;
                    case AIProvider.OpenAI: OpenAIModelId = ProviderModelNormalizer.Normalize(AIProvider.OpenAI, value); break;
                    case AIProvider.Anthropic: AnthropicModelId = ProviderModelNormalizer.Normalize(AIProvider.Anthropic, value); break;
                    case AIProvider.OpenRouter: OpenRouterModelId = ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, value); break;
                    case AIProvider.DeepSeek: DeepSeekModelId = ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, value); break;
                    case AIProvider.Gemini: GeminiModelId = ProviderModelNormalizer.Normalize(AIProvider.Gemini, value); break;
                    case AIProvider.Groq: GroqModelId = ProviderModelNormalizer.Normalize(AIProvider.Groq, value); break;
                    case AIProvider.ClaudeCodeSubscription: ClaudeCodeModelId = value; break;
                    case AIProvider.OpenAICodexSubscription: OpenAICodexModelId = value; break;
                }
            }
        }

        // Effective model is computed and not user-editable; omit from schema to avoid UI binding attempts
        public string EffectiveModel => ModelSelection;

        // Advanced: manual model override for cloud providers
        [FieldDefinition(23, Label = "Manual Model ID (override)", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Optional: exact API model ID to use for cloud providers (e.g., openai/gpt-4o, anthropic/claude-3.5-sonnet, qwen/qwen-2.5-72b-instruct). If set, this overrides the selection above.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#manual-model-override")]
        public string? ManualModelId { get; set; }

        // Anthropic/OpenRouter extended thinking controls
        [FieldDefinition(24, Label = "Thinking Mode", Type = FieldType.Select, SelectOptions = typeof(ThinkingMode), Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Controls Claude extended thinking.\n- Off: Never enable thinking\n- Auto: Enable for Anthropic provider; for OpenRouter auto-switches to :thinking variant on Anthropic routes\n- On: Force enable (same as Auto for now).\nNote: With Auto/On, OpenRouter Anthropic models use ':thinking' automatically; direct Anthropic adds 'thinking' with optional 'budget_tokens' (see next field).",
            HelpLink = "https://docs.anthropic.com/")]
        public ThinkingMode ThinkingMode { get; set; } = ThinkingMode.Off;

        [FieldDefinition(25, Label = "Thinking Budget Tokens", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Optional token budget for thinking. Leave 0 to let Anthropic decide. Typical: 2000-8000.",
            HelpLink = "https://docs.anthropic.com/")]
        public int ThinkingBudgetTokens { get; set; } = 0;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
