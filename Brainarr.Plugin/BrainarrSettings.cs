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


    public class BrainarrSettings : IImportListSettings
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

        private SamplingShape _samplingShape = SamplingShape.Default;

        // Advanced sampling controls (visible under Advanced settings).
        [JsonPropertyName("sampling_shape")]
        public SamplingShape SamplingShape
        {
            get => _samplingShape;
            set => _samplingShape = value ?? SamplingShape.Default;
        }


        [JsonIgnore]
        internal SamplingShape EffectiveSamplingShape => _samplingShape ?? SamplingShape.Default;

        private SamplingShape GetSamplingShape() => _samplingShape ?? SamplingShape.Default;

        private void UpdateSamplingShape(Func<SamplingShape, SamplingShape> mutator)
        {
            _samplingShape = mutator(GetSamplingShape()) ?? SamplingShape.Default;
        }

        private static SamplingShape.ModeDistribution UpdateDistribution(
            SamplingShape.ModeDistribution distribution,
            int? topPercent = null,
            int? recentPercent = null)
        {
            var top = Math.Clamp(topPercent ?? distribution.TopPercent, 0, 100);
            var recent = Math.Clamp(recentPercent ?? distribution.RecentPercent, 0, 100 - top);
            return new SamplingShape.ModeDistribution(top, recent);
        }

        private void UpdateArtistDistribution(DiscoveryMode mode, int? topPercent = null, int? recentPercent = null)
        {
            UpdateSamplingShape(shape =>
            {
                var artist = shape.Artist ?? SamplingShape.ModeShape.CreateArtistDefaults();
                var updatedArtist = mode switch
                {
                    DiscoveryMode.Similar => artist with { Similar = UpdateDistribution(artist.Similar, topPercent, recentPercent) },
                    DiscoveryMode.Adjacent => artist with { Adjacent = UpdateDistribution(artist.Adjacent, topPercent, recentPercent) },
                    DiscoveryMode.Exploratory => artist with { Exploratory = UpdateDistribution(artist.Exploratory, topPercent, recentPercent) },
                    _ => artist
                };

                return shape with { Artist = updatedArtist };
            });
        }

        private void UpdateAlbumDistribution(DiscoveryMode mode, int? topPercent = null, int? recentPercent = null)
        {
            UpdateSamplingShape(shape =>
            {
                var album = shape.Album ?? SamplingShape.ModeShape.CreateAlbumDefaults();
                var updatedAlbum = mode switch
                {
                    DiscoveryMode.Similar => album with { Similar = UpdateDistribution(album.Similar, topPercent, recentPercent) },
                    DiscoveryMode.Adjacent => album with { Adjacent = UpdateDistribution(album.Adjacent, topPercent, recentPercent) },
                    DiscoveryMode.Exploratory => album with { Exploratory = UpdateDistribution(album.Exploratory, topPercent, recentPercent) },
                    _ => album
                };

                return shape with { Album = updatedAlbum };
            });
        }

        private const string SamplingShapeSection = "Sampling Shape (Advanced)";

        [FieldDefinition(160, Label = "Artist Similar Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Similar.")]
        public int SamplingArtistSimilarTopPercent
        {
            get => GetSamplingShape().Artist.Similar.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Similar, topPercent: value);
        }

        [FieldDefinition(161, Label = "Artist Similar Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Similar.")]
        public int SamplingArtistSimilarRecentPercent
        {
            get => GetSamplingShape().Artist.Similar.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Similar, recentPercent: value);
        }

        [FieldDefinition(162, Label = "Artist Adjacent Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Adjacent.")]
        public int SamplingArtistAdjacentTopPercent
        {
            get => GetSamplingShape().Artist.Adjacent.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Adjacent, topPercent: value);
        }

        [FieldDefinition(163, Label = "Artist Adjacent Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Adjacent.")]
        public int SamplingArtistAdjacentRecentPercent
        {
            get => GetSamplingShape().Artist.Adjacent.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Adjacent, recentPercent: value);
        }

        [FieldDefinition(164, Label = "Artist Exploratory Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Exploratory.")]
        public int SamplingArtistExploratoryTopPercent
        {
            get => GetSamplingShape().Artist.Exploratory.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Exploratory, topPercent: value);
        }

        [FieldDefinition(165, Label = "Artist Exploratory Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Exploratory.")]
        public int SamplingArtistExploratoryRecentPercent
        {
            get => GetSamplingShape().Artist.Exploratory.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Exploratory, recentPercent: value);
        }

        [FieldDefinition(166, Label = "Album Similar Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Similar.")]
        public int SamplingAlbumSimilarTopPercent
        {
            get => GetSamplingShape().Album.Similar.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Similar, topPercent: value);
        }

        [FieldDefinition(167, Label = "Album Similar Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Similar.")]
        public int SamplingAlbumSimilarRecentPercent
        {
            get => GetSamplingShape().Album.Similar.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Similar, recentPercent: value);
        }

        [FieldDefinition(168, Label = "Album Adjacent Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Adjacent.")]
        public int SamplingAlbumAdjacentTopPercent
        {
            get => GetSamplingShape().Album.Adjacent.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Adjacent, topPercent: value);
        }

        [FieldDefinition(169, Label = "Album Adjacent Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Adjacent.")]
        public int SamplingAlbumAdjacentRecentPercent
        {
            get => GetSamplingShape().Album.Adjacent.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Adjacent, recentPercent: value);
        }

        [FieldDefinition(170, Label = "Album Exploratory Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Exploratory.")]
        public int SamplingAlbumExploratoryTopPercent
        {
            get => GetSamplingShape().Album.Exploratory.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Exploratory, topPercent: value);
        }

        [FieldDefinition(171, Label = "Album Exploratory Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Exploratory.")]
        public int SamplingAlbumExploratoryRecentPercent
        {
            get => GetSamplingShape().Album.Exploratory.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Exploratory, recentPercent: value);
        }

        [FieldDefinition(172, Label = "Minimum Albums per Artist", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Floor for albums per artist after compression. Increase to emphasise depth per artist.")]
        public int SamplingMaxAlbumsPerGroupFloor
        {
            get => GetSamplingShape().MaxAlbumsPerGroupFloor;
            set => UpdateSamplingShape(shape => shape with { MaxAlbumsPerGroupFloor = Math.Clamp(value, 0, 10) });
        }

        [FieldDefinition(173, Label = "Relaxed Match Inflation", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Maximum multiplier applied when relaxed style matching expands artist/album pools.")]
        public double SamplingMaxRelaxedInflation
        {
            get => GetSamplingShape().MaxRelaxedInflation;
            set => UpdateSamplingShape(shape => shape with { MaxRelaxedInflation = Math.Clamp(value, 1.0, 5.0) });
        }

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

        // No backward-compat properties; canonical fields are *ModelId

        // Auto-detect model (show for all providers)
        [FieldDefinition(4, Label = "Auto-Detect Model", Type = FieldType.Checkbox, HelpText = "Automatically detect and select best available model", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#auto-detect-model")]
        public bool AutoDetectModel { get; set; }

        // Discovery Settings
        [FieldDefinition(5, Label = "Recommendations", Type = FieldType.Number,
            HelpText = "Number of items to return each run (1-50). Brainarr treats this as your target and tops-up iteratively.\nTip: Start with 5-10 and increase if you like the results", HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendations")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(6, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode),
            HelpText = "How adventurous should recommendations be?\n- Similar: Stay close to current taste\n- Adjacent: Explore related genres\n- Exploratory: Discover new genres",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#discovery-mode")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(7, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\n- Minimal: Fast, less context (good for local models)\n- Balanced: Default, optimal balance\n- Comprehensive: Maximum context (best for GPT-4/Claude)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#library-sampling")]
        public SamplingStrategy SamplingStrategy { get; set; }

        [FieldDefinition(8, Label = "Recommendation Type", Type = FieldType.Select, SelectOptions = typeof(RecommendationMode),
            HelpText = "Control what gets recommended:\n- Specific Albums: Recommend individual albums to import\n- Artists: Recommend artists (Lidarr will import ALL their albums)\n\nTip: Choose 'Artists' for comprehensive library building, 'Specific Albums' for targeted additions",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#recommendation-type")]
        public RecommendationMode RecommendationMode { get; set; }

        [FieldDefinition(9, Label = "Backfill Strategy (Default: Aggressive)", Type = FieldType.Select, SelectOptions = typeof(BackfillStrategy),
            HelpText = "One simple setting to control top-up behavior when under target.\n- Off: No top-up passes (first batch only)\n- Standard: A few passes + initial oversampling\n- Aggressive (Default): More passes, relaxed gating, tries to guarantee target\nNote: Advanced top-up controls are hidden in v1.2.1; strategy is sufficient for most users.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#backfill-strategy")]
        public BackfillStrategy BackfillStrategy { get; set; }

        // Internal: optional hard cap for top-up iterations (no UI field yet)
        public int MaxTopUpIterations { get; set; } = 0;

        // Lidarr Integration (Hidden from UI, set by Lidarr)
        public string BaseUrl
        {
            get => Provider == AIProvider.Ollama ? OllamaUrl : LMStudioUrl;
            set { /* Handled by provider-specific URLs */ }
        }

        // Model Detection Results (populated during test)
        public List<string> DetectedModels { get; set; } = new List<string>();

        // Back-compat: proxy to AutoDetectModel (remove duplicate behavior)
        public bool EnableAutoDetection
        {
            get => AutoDetectModel;
            set => AutoDetectModel = value;
        }

        // Additional missing properties
        public bool EnableFallbackModel { get; set; } = true;
        public string FallbackModel { get; set; } = "qwen2.5:latest";
        public bool EnableLibraryAnalysis { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);
        [FieldDefinition(17, Label = "Top-Up When Under Target", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "If under target, request additional recommendations with feedback to fill the gap.\nFor local providers (Ollama/LM Studio) this runs by default.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#iterative-top-up")]
        public bool EnableIterativeRefinement { get; set; } = false;

        // Iteration Hysteresis (Advanced)
        [FieldDefinition(18, Label = "Top-Up Max Iterations", Type = FieldType.Number, Advanced = true,
            HelpText = "Maximum top-up iterations before stopping.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeMaxIterations { get; set; } = 3;

        [FieldDefinition(19, Label = "Zero‑Success Stop After", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop iterative top‑up after this many zero‑unique iterations.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeZeroSuccessStopThreshold { get; set; } = 1;

        [FieldDefinition(20, Label = "Low‑Success Stop After", Type = FieldType.Number, Advanced = true,
            HelpText = "Stop iterative top‑up after this many low‑success iterations (mode‑adjusted).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeLowSuccessStopThreshold { get; set; } = 2;

        [FieldDefinition(21, Label = "Top‑Up Cooldown (ms)", Type = FieldType.Number, Advanced = true,
            HelpText = "Cooldown (milliseconds) on early stop to reduce churn (local providers).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#hysteresis-controls")]
        public int IterativeCooldownMs { get; set; } = 1000;

        // Simplified control replacing individual stop thresholds
        [FieldDefinition(23, Label = "Top-Up Stop Sensitivity", Type = FieldType.Select, SelectOptions = typeof(StopSensitivity), Advanced = true,
            HelpText = "Controls how quickly top-up attempts stop: Strict (stop early), Normal, Lenient (default, allow more attempts). Threshold fields apply as minimums.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#iterative-top-up")]
        [JsonConverter(typeof(StopSensitivityJsonConverter))]
        public StopSensitivity TopUpStopSensitivity { get; set; } = StopSensitivity.Lenient;

        // Request timeout for AI provider calls (seconds)
        [FieldDefinition(26, Label = "AI Request Timeout (s)", Type = FieldType.Number,
            HelpText = "Timeout for provider requests in seconds. Increase for slow local LLMs.\nNote: For Ollama/LM Studio, Brainarr uses 360s if this is set near default (≤30s).",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#timeouts")]
        public int AIRequestTimeoutSeconds { get; set; } = BrainarrConstants.DefaultAITimeout;

        // OpenAI-compatible providers
        [FieldDefinition(30, Label = "Prefer Structured JSON (schema)", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Use JSON Schema structured responses when supported (OpenAI/OpenRouter/Groq/DeepSeek/Perplexity). Disable if your gateway has issues with structured outputs.")]
        public bool PreferStructuredJsonForChat { get; set; } = true;

        // LM Studio tuning (advanced)
        [FieldDefinition(28, Label = "LM Studio Temperature", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Sampling temperature for LM Studio (0.0-2.0). Lower is more deterministic; 0.3–0.7 recommended for curation.")]
        public double LMStudioTemperature { get; set; } = 0.5;

        [FieldDefinition(29, Label = "LM Studio Max Tokens", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Maximum tokens to generate for LM Studio responses. Increase if responses get cut off.")]
        public int LMStudioMaxTokens { get; set; } = 2000;

        // Advanced Validation Settings
        [FieldDefinition(24, Label = "Custom Filter Patterns", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Additional patterns to filter out AI hallucinations (comma-separated)\nExample: '(alternate take), (radio mix), (demo version)'\nNote: Be careful not to filter legitimate albums!")]
        public string CustomFilterPatterns { get; set; } = string.Empty;

        [FieldDefinition(10, Label = "Enable Strict Validation", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Apply stricter validation rules to reduce false positives\n- Filters more aggressively\n- May block some legitimate albums")]
        public bool EnableStrictValidation { get; set; }

        [FieldDefinition(11, Label = "Enable Debug Logging", Type = FieldType.Checkbox, Advanced = true,
                    HelpText = "Enable detailed logging for troubleshooting\nWarning: May include sensitive prompt/request/response snippets. Do not enable in production.\nNote: Creates verbose logs")]
        public bool EnableDebugLogging { get; set; }

        [FieldDefinition(25, Label = "Log Per-Item Decisions", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Log each accepted/rejected recommendation.\n- Rejections: Always Info (with reason)\n- Accepted: Info only when Debug Logging is enabled\nDisable to reduce log noise — aggregate summaries remain.\nLearn more: see Troubleshooting guide (link below)",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Troubleshooting#reading-brainarr-logs")]
        public bool LogPerItemDecisions { get; set; } = true;

        // Advanced: Concurrency (per-model) overrides for limiter (hidden)
        [FieldDefinition(27, Label = "Max Concurrent Per Model (Cloud)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Override default concurrency per model for cloud providers (OpenAI/Anthropic/OpenRouter/Groq/Perplexity/DeepSeek/Gemini). Leave blank to use defaults.")]
        public int? MaxConcurrentPerModelCloud { get; set; }

        [FieldDefinition(27, Label = "Max Concurrent Per Model (Local)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Override default concurrency per model for local providers (Ollama/LM Studio). Leave blank to use defaults.")]
        public int? MaxConcurrentPerModelLocal { get; set; }

        // Advanced: Adaptive throttling (429) controls (hidden)
        [FieldDefinition(27, Label = "Enable Adaptive Throttling", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Automatically reduce per-model concurrency for a short time when 429 (TooManyRequests) is observed.")]
        public bool EnableAdaptiveThrottling { get; set; } = false;

        [FieldDefinition(27, Label = "Adaptive Throttle Seconds", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "How long to keep reduced concurrency after 429 (seconds). Default 60.")]
        public int AdaptiveThrottleSeconds { get; set; } = 60;

        [FieldDefinition(27, Label = "Adaptive Throttle Cap (Cloud)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Temporary per-model concurrency cap for cloud providers after 429. Default 2.")]
        public int? AdaptiveThrottleCloudCap { get; set; }

        // ===== Music Styles (dynamic TagSelect) =====
        [FieldDefinition(34, Label = "Music Styles", Type = FieldType.TagSelect,
            HelpText = "Select one or more styles (aliases supported). Leave empty to use your library profile.",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Styles",
            SelectOptionsProviderAction = "styles/getoptions")]
        public IEnumerable<string> StyleFilters { get; set; } = Array.Empty<string>();

        // Hidden/advanced knobs related to styles & token budgets
        [FieldDefinition(35, Label = "Max Selected Styles", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Soft cap for number of selected styles applied in prompts (default 10). Exceeding selections are trimmed with a log warning.")]
        public int MaxSelectedStyles { get; set; } = 10;

        [FieldDefinition(36, Label = "Comprehensive Token Budget Override", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Optional override for Comprehensive prompt token budget. Leave blank to auto-detect from model.")]
        public int? ComprehensiveTokenBudgetOverride { get; set; }

        [FieldDefinition(37, Label = "Relax Style Matching", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "When enabled, allow parent/adjacent styles as fallback. Default OFF for strict matching.")]
        public bool RelaxStyleMatching { get; set; } = false;

        [FieldDefinition(27, Label = "Adaptive Throttle Cap (Local)", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
            HelpText = "Temporary per-model concurrency cap for local providers after 429. Default 8.")]
        public int? AdaptiveThrottleLocalCap { get; set; }

        // Safety Gates
        [FieldDefinition(12, Label = "Minimum Confidence", Type = FieldType.Number, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Drop or queue items below this confidence (0.0-1.0)",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public double MinConfidence { get; set; } = 0.7;

        [FieldDefinition(13, Label = "Require MusicBrainz IDs", Type = FieldType.Checkbox, Advanced = true,
            HelpText = "Require MBIDs before adding. Items without MBIDs are sent to Review Queue",
            HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates")]
        public bool RequireMbids { get; set; } = true;

        // Aggressive completion options (Advanced)
        [FieldDefinition(22, Label = "Guarantee Exact Target", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Try harder to return exactly the requested number of items.\nIf still under target after normal top-up, Brainarr will iterate more aggressively and, in Artist mode, may promote name-only artists to fill the gap.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#guarantee-exact-target")]
        public bool GuaranteeExactTarget { get; set; } = false;

        [FieldDefinition(14, Label = "Queue Borderline Items", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Send low-confidence or missing-MBID items to the Review Queue instead of dropping them",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings#safety-gates",
                    Section = "Review Queue")]
        public bool QueueBorderlineItems { get; set; } = true;

        // Review Queue UI integration
        // Use TagSelect (multi-select with chips) to show options from the provider
        [FieldDefinition(15, Label = "Approve Suggestions", Type = FieldType.TagSelect, Hidden = HiddenType.Hidden,
                    HelpText = "Pick items from your Review Queue to approve.\n• Save to apply on the next sync (or use the 'review/apply' action to apply immediately).\n• After a successful apply, selections auto‑clear and are persisted when supported by your host.",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
                    Placeholder = "Search or select pending items…",
                    Section = "Review Queue",
                    SelectOptionsProviderAction = "review/getoptions")]
        public IEnumerable<string> ReviewApproveKeys { get; set; } = Array.Empty<string>();

        [FieldDefinition(16, Label = "Review Summary", Type = FieldType.TagSelect, Hidden = HiddenType.Hidden,
                    HelpText = "Quick overview of queue counts (informational)",
                    HelpLink = "https://github.com/RicherTunes/Brainarr/wiki/Review-Queue",
                    Placeholder = "Pending / Accepted / Rejected / Never…",
                    Section = "Review Queue",
                    SelectOptionsProviderAction = "review/getsummaryoptions")]
        public IEnumerable<string> ReviewSummary { get; set; } = Array.Empty<string>();

        // Observability (hidden preview)
        [FieldDefinition(16, Label = "Observability (Preview)", Type = FieldType.TagSelect, Advanced = true,
                    HelpText = "Compact preview of provider/model latency, errors and throttles.",
                    HelpLink = "observability/html",
                    Placeholder = "provider:model — p95, errors, 429 (last 15m)",
                    Section = "Observability",
                    SelectOptionsProviderAction = "observability/getoptions")]
        public IEnumerable<string> ObservabilityPreview { get; set; } = Array.Empty<string>();

        // Default filters for preview (hidden)
        [FieldDefinition(32, Label = "Observability Provider Filter", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Optional default provider filter for Observability preview (e.g., 'openai', 'ollama').")]
        public string ObservabilityProviderFilter { get; set; }

        [FieldDefinition(33, Label = "Observability Model Filter", Type = FieldType.Textbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Optional default model filter for Observability preview (e.g., 'gpt-4o-mini').")]
        public string ObservabilityModelFilter { get; set; }

        // Feature flag: quickly disable Observability UI without code changes (hidden)
        [FieldDefinition(31, Label = "Enable Observability Preview", Type = FieldType.Checkbox, Advanced = true, Hidden = HiddenType.Hidden,
                    HelpText = "Toggle the Observability (Preview) UI and endpoints without redeploying.")]
        public bool EnableObservabilityPreview { get; set; } = true;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        private string? SanitizeApiKey(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return apiKey;

            // Remove any potential whitespace or control characters
            apiKey = apiKey?.Trim();

            // Basic validation to prevent injection
            if (apiKey != null && apiKey.Length > 500)
            {
                throw new ArgumentException("API key exceeds maximum allowed length");
            }

            return apiKey;
        }

        /// <summary>
        /// Gets provider-specific settings for configuration.
        /// </summary>
        public Dictionary<string, object> GetProviderSettings(AIProvider provider)
        {
            var settings = new Dictionary<string, object>();

            switch (provider)
            {
                case AIProvider.Ollama:
                    settings["url"] = OllamaUrl;
                    settings["model"] = OllamaModel;
                    break;
                case AIProvider.LMStudio:
                    settings["url"] = LMStudioUrl;
                    settings["model"] = LMStudioModel;
                    break;
                case AIProvider.OpenAI:
                    settings["apiKey"] = OpenAIApiKey;
                    settings["model"] = OpenAIModelId;
                    break;
                case AIProvider.Anthropic:
                    settings["apiKey"] = AnthropicApiKey;
                    settings["model"] = AnthropicModelId;
                    break;
                case AIProvider.Perplexity:
                    settings["apiKey"] = PerplexityApiKey;
                    settings["model"] = PerplexityModelId;
                    break;
                case AIProvider.OpenRouter:
                    settings["apiKey"] = OpenRouterApiKey;
                    settings["model"] = OpenRouterModelId;
                    break;
                case AIProvider.DeepSeek:
                    settings["apiKey"] = DeepSeekApiKey;
                    settings["model"] = DeepSeekModelId;
                    break;
                case AIProvider.Gemini:
                    settings["apiKey"] = GeminiApiKey;
                    settings["model"] = GeminiModelId;
                    break;
                case AIProvider.Groq:
                    settings["apiKey"] = GroqApiKey;
                    settings["model"] = GroqModelId;
                    break;
            }

            return settings;
        }


        /// <summary>
        /// Gets the default model for a specific provider.
        /// </summary>
        private string? GetCurrentProviderModel()
        {
            return Provider switch
            {
                AIProvider.Ollama => _ollamaModel,
                AIProvider.LMStudio => _lmStudioModel,
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                _ => null
            };
        }

        private void ClearCurrentProviderModel()
        {
            // Clear the model for the current provider to reset to default
            switch (_provider)
            {
                case AIProvider.Ollama:
                    _ollamaModel = null;
                    break;
                case AIProvider.LMStudio:
                    _lmStudioModel = null;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = null;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = null;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = null;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = null;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = null;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = null;
                    break;
                case AIProvider.Groq:
                    GroqModelId = null;
                    break;
            }
        }

        private void ClearPreviousProviderModel()
        {
            // Clear the model for the previous provider when switching away
            if (_previousProvider.HasValue)
            {
                switch (_previousProvider.Value)
                {
                    case AIProvider.Ollama:
                        _ollamaModel = null;
                        break;
                    case AIProvider.LMStudio:
                        _lmStudioModel = null;
                        break;
                    case AIProvider.Perplexity:
                        PerplexityModelId = null;
                        break;
                    case AIProvider.OpenAI:
                        OpenAIModelId = null;
                        break;
                    case AIProvider.Anthropic:
                        AnthropicModelId = null;
                        break;
                    case AIProvider.OpenRouter:
                        OpenRouterModelId = null;
                        break;
                    case AIProvider.DeepSeek:
                        DeepSeekModelId = null;
                        break;
                    case AIProvider.Gemini:
                        GeminiModelId = null;
                        break;
                    case AIProvider.Groq:
                        GroqModelId = null;
                        break;
                }
            }
        }

        private string GetDefaultModelForProvider(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => BrainarrConstants.DefaultOllamaModel,
                AIProvider.LMStudio => BrainarrConstants.DefaultLMStudioModel,
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                _ => "Default"
            };
        }

        // Polymorphic methods to get/set provider-specific configuration
        public string? GetModelForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaModel,
                AIProvider.LMStudio => LMStudioModel,
                AIProvider.OpenAI => ProviderModelNormalizer.Normalize(AIProvider.OpenAI, string.IsNullOrEmpty(OpenAIModelId) ? BrainarrConstants.DefaultOpenAIModel : OpenAIModelId),
                AIProvider.Anthropic => ProviderModelNormalizer.Normalize(AIProvider.Anthropic, string.IsNullOrEmpty(AnthropicModelId) ? BrainarrConstants.DefaultAnthropicModel : AnthropicModelId),
                AIProvider.Perplexity => ProviderModelNormalizer.Normalize(AIProvider.Perplexity, string.IsNullOrEmpty(PerplexityModelId) ? BrainarrConstants.DefaultPerplexityModel : PerplexityModelId),
                AIProvider.OpenRouter => ProviderModelNormalizer.Normalize(AIProvider.OpenRouter, string.IsNullOrEmpty(OpenRouterModelId) ? BrainarrConstants.DefaultOpenRouterModel : OpenRouterModelId),
                AIProvider.DeepSeek => ProviderModelNormalizer.Normalize(AIProvider.DeepSeek, string.IsNullOrEmpty(DeepSeekModelId) ? BrainarrConstants.DefaultDeepSeekModel : DeepSeekModelId),
                AIProvider.Gemini => ProviderModelNormalizer.Normalize(AIProvider.Gemini, string.IsNullOrEmpty(GeminiModelId) ? BrainarrConstants.DefaultGeminiModel : GeminiModelId),
                AIProvider.Groq => ProviderModelNormalizer.Normalize(AIProvider.Groq, string.IsNullOrEmpty(GroqModelId) ? BrainarrConstants.DefaultGroqModel : GroqModelId),
                _ => null
            };
        }

        public void SetModelForProvider(string? model)
        {
            switch (Provider)
            {
                case AIProvider.Ollama:
                    OllamaModel = model;
                    break;
                case AIProvider.LMStudio:
                    LMStudioModel = model;
                    break;
                case AIProvider.OpenAI:
                    OpenAIModelId = model;
                    break;
                case AIProvider.Anthropic:
                    AnthropicModelId = model;
                    break;
                case AIProvider.Perplexity:
                    PerplexityModelId = model;
                    break;
                case AIProvider.OpenRouter:
                    OpenRouterModelId = model;
                    break;
                case AIProvider.DeepSeek:
                    DeepSeekModelId = model;
                    break;
                case AIProvider.Gemini:
                    GeminiModelId = model;
                    break;
                case AIProvider.Groq:
                    GroqModelId = model;
                    break;
            }
        }

        public string? GetApiKeyForProvider()
        {
            return Provider switch
            {
                AIProvider.OpenAI => OpenAIApiKey,
                AIProvider.Anthropic => AnthropicApiKey,
                AIProvider.Perplexity => PerplexityApiKey,
                AIProvider.OpenRouter => OpenRouterApiKey,
                AIProvider.DeepSeek => DeepSeekApiKey,
                AIProvider.Gemini => GeminiApiKey,
                AIProvider.Groq => GroqApiKey,
                _ => null
            };
        }

        public string? GetBaseUrlForProvider()
        {
            return Provider switch
            {
                AIProvider.Ollama => OllamaUrl,
                AIProvider.LMStudio => LMStudioUrl,
                _ => null
            };
        }

        private static string NormalizeHttpUrlOrOriginal(string value) => Configuration.UrlValidator.NormalizeHttpUrlOrOriginal(value);

        // ===== Backfill mapping helpers =====

        public IterationProfile GetIterationProfile()
        {
            // Map simple BackfillStrategy to effective iteration settings
            switch (BackfillStrategy)
            {
                case BackfillStrategy.Off:
                    return new IterationProfile
                    {
                        EnableRefinement = false,
                        MaxIterations = 0,
                        ZeroStop = 0,
                        LowStop = 0,
                        CooldownMs = 0,
                        GuaranteeExactTarget = false
                    };

                case BackfillStrategy.Aggressive:
                    return new IterationProfile
                    {
                        EnableRefinement = true,
                        MaxIterations = MaxTopUpIterations > 0 ? MaxTopUpIterations : IterativeMaxIterations,
                        ZeroStop = Math.Max(3, MapZeroStop()),
                        LowStop = Math.Max(8, MapLowStop()),
                        CooldownMs = Math.Min(500, Math.Max(0, IterativeCooldownMs)),
                        GuaranteeExactTarget = true
                    };

                case BackfillStrategy.Standard:
                default:
                    return new IterationProfile
                    {
                        EnableRefinement = true,
                        MaxIterations = MaxTopUpIterations > 0 ? MaxTopUpIterations : IterativeMaxIterations,
                        ZeroStop = Math.Max(1, MapZeroStop()),
                        LowStop = Math.Max(4, MapLowStop()),
                        CooldownMs = IterativeCooldownMs,
                        GuaranteeExactTarget = GuaranteeExactTarget
                    };
            }
        }

        private int MapZeroStop()
        {
            var mapped = TopUpStopSensitivity switch
            {
                StopSensitivity.Strict => 1,
                StopSensitivity.Lenient => 2,
                _ => 1
            };
            return Math.Max(mapped, IterativeZeroSuccessStopThreshold);
        }

        private int MapLowStop()
        {
            var mapped = TopUpStopSensitivity switch
            {
                StopSensitivity.Strict => 1,
                StopSensitivity.Lenient => 4,
                _ => 2
            };
            return Math.Max(mapped, IterativeLowSuccessStopThreshold);
        }

        public bool IsBackfillEnabled() => GetIterationProfile().EnableRefinement;

        private static bool IsPerplexityModelValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            var lower = v.Replace('_', '-').ToLowerInvariant();
            if (lower.StartsWith("sonar-")) return true;
            if (lower.StartsWith("llama-3.1-sonar")) return true;
            // Accept enum-style names
            return Enum.TryParse(typeof(PerplexityModelKind), v, ignoreCase: true, out _);
        }

        private static bool LooksLikeLocalModelValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            // LM Studio/Ollama models often contain a slash or a tag (:) or the sentinel "local-model"
            return v.Equals("local-model", StringComparison.OrdinalIgnoreCase) || v.Contains('/') || v.Contains(':');
        }
    }

    public class IterationProfile
    {
        public bool EnableRefinement { get; set; }
        public int MaxIterations { get; set; }
        public int ZeroStop { get; set; }
        public int LowStop { get; set; }
        public int CooldownMs { get; set; }
        public bool GuaranteeExactTarget { get; set; }
    }

}
