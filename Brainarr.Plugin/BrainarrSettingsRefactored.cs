using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Configuration.Providers;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    /// <summary>
    /// Refactored BrainarrSettings that acts as an aggregate root.
    /// Uses provider-specific settings instead of a monolithic approach.
    /// </summary>
    public class BrainarrSettingsRefactored : IImportListSettings
    {
        private static readonly BrainarrSettingsRefactoredValidator Validator = new BrainarrSettingsRefactoredValidator();
        private static readonly ProviderSettingsFactory ProviderFactory = new ProviderSettingsFactory();
        
        private readonly Dictionary<AIProvider, IProviderSettings> _providerSettings;

        public BrainarrSettingsRefactored()
        {
            // Initialize with default settings for all providers
            _providerSettings = new Dictionary<AIProvider, IProviderSettings>();
            foreach (var provider in ProviderFactory.GetSupportedProviders())
            {
                _providerSettings[provider] = ProviderFactory.CreateSettings(provider);
            }
            
            // Set sensible defaults
            Provider = AIProvider.Ollama; // Default to local provider
            MaxRecommendations = BrainarrConstants.DefaultRecommendations;
            DiscoveryMode = DiscoveryMode.Adjacent;
            SamplingStrategy = SamplingStrategy.Balanced;
            RecommendationMode = RecommendationMode.SpecificAlbums;
            AutoDetectModel = true;
        }

        // ====== CORE SETTINGS ======
        
        [FieldDefinition(0, Label = "AI Provider", Type = FieldType.Select, SelectOptions = typeof(AIProvider), 
            HelpText = "Choose your AI provider:\\n🏠 LOCAL (Private): Ollama, LM Studio - Your data stays private\\n🌐 GATEWAY: OpenRouter - Access 200+ models with one key\\n💰 BUDGET: DeepSeek, Gemini - Low cost or free\\n⚡ FAST: Groq - Ultra-fast responses\\n🤖 PREMIUM: OpenAI, Anthropic - Best quality")]
        public AIProvider Provider { get; set; }

        [FieldDefinition(1, Label = "Recommendations", Type = FieldType.Number, 
            HelpText = "Number of albums per sync (1-50, default: 10)\\n💡 Start with 5-10 and increase if you like the results")]
        public int MaxRecommendations { get; set; }

        [FieldDefinition(2, Label = "Discovery Mode", Type = FieldType.Select, SelectOptions = typeof(DiscoveryMode), 
            HelpText = "How adventurous should recommendations be?\\n• Similar: Stay close to current taste\\n• Adjacent: Explore related genres\\n• Exploratory: Discover new genres")]
        public DiscoveryMode DiscoveryMode { get; set; }

        [FieldDefinition(3, Label = "Library Sampling", Type = FieldType.Select, SelectOptions = typeof(SamplingStrategy),
            HelpText = "How much of your library to include in AI prompts\\n• Minimal: Fast, less context (good for local models)\\n• Balanced: Default, optimal balance\\n• Comprehensive: Maximum context (best for GPT-4/Claude)")]
        public SamplingStrategy SamplingStrategy { get; set; }

        [FieldDefinition(4, Label = "Recommendation Type", Type = FieldType.Select, SelectOptions = typeof(RecommendationMode),
            HelpText = "Control what gets recommended:\\n• Specific Albums: Recommend individual albums to import\\n• Artists: Recommend artists (Lidarr will import ALL their albums)\\n\\n💡 Choose 'Artists' for comprehensive library building, 'Specific Albums' for targeted additions")]
        public RecommendationMode RecommendationMode { get; set; }

        [FieldDefinition(5, Label = "Auto-Detect Model", Type = FieldType.Checkbox, 
            HelpText = "Automatically detect and select best available model")]
        public bool AutoDetectModel { get; set; }

        // ====== PROVIDER-SPECIFIC SETTINGS ACCESS ======

        /// <summary>
        /// Gets the settings for the currently active provider.
        /// This eliminates switch statements for provider-specific operations.
        /// </summary>
        public IProviderSettings GetActiveProviderSettings()
        {
            return _providerSettings[Provider];
        }

        /// <summary>
        /// Gets settings for a specific provider.
        /// </summary>
        public IProviderSettings GetProviderSettings(AIProvider provider)
        {
            return _providerSettings[provider];
        }

        /// <summary>
        /// Updates settings for a specific provider.
        /// </summary>
        public void UpdateProviderSettings(AIProvider provider, IProviderSettings settings)
        {
            if (settings.ProviderType != provider)
            {
                throw new ArgumentException($"Settings type {settings.ProviderType} does not match provider {provider}");
            }
            _providerSettings[provider] = settings;
        }

        // ====== CONVENIENCE PROPERTIES (Backward Compatibility) ======
        // These delegate to the provider settings to eliminate switch statements

        /// <summary>
        /// Gets or sets the API key for the active provider (if it's a cloud provider).
        /// </summary>
        public string? ApiKey 
        { 
            get => GetActiveProviderSettings().GetApiKey();
            set
            {
                // This is more complex for setting, but we can handle it polymorphically
                var settings = GetActiveProviderSettings();
                if (settings is CloudProviderSettings<PerplexityProviderSettings> perplexitySettings)
                    perplexitySettings.ApiKey = value ?? string.Empty;
                else if (settings is CloudProviderSettings<OpenAIProviderSettings> openAISettings)
                    openAISettings.ApiKey = value ?? string.Empty;
                // ... etc for other cloud providers
            }
        }

        /// <summary>
        /// Gets or sets the model for the active provider.
        /// </summary>
        public string? ModelSelection 
        { 
            get => GetActiveProviderSettings().GetModel();
            set
            {
                var settings = GetActiveProviderSettings();
                // Similar polymorphic handling for setting models
                if (settings is OllamaProviderSettings ollamaSettings)
                    ollamaSettings.Model = value ?? string.Empty;
                else if (settings is LMStudioProviderSettings lmStudioSettings)
                    lmStudioSettings.Model = value ?? string.Empty;
                // ... etc
            }
        }

        /// <summary>
        /// Gets or sets the base URL for the active provider (if it's a local provider).
        /// </summary>
        public string? BaseUrl 
        { 
            get => GetActiveProviderSettings().GetBaseUrl();
            set
            {
                var settings = GetActiveProviderSettings();
                if (settings is OllamaProviderSettings ollamaSettings)
                    ollamaSettings.Url = value ?? BrainarrConstants.DefaultOllamaUrl;
                else if (settings is LMStudioProviderSettings lmStudioSettings)
                    lmStudioSettings.Url = value ?? BrainarrConstants.DefaultLMStudioUrl;
            }
        }

        // ====== VALIDATION ======

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }

    /// <summary>
    /// Validator for the refactored BrainarrSettings.
    /// Uses provider-specific validation instead of switch statements.
    /// </summary>
    public class BrainarrSettingsRefactoredValidator : AbstractValidator<BrainarrSettingsRefactored>
    {
        public BrainarrSettingsRefactoredValidator()
        {
            RuleFor(c => c.MaxRecommendations)
                .InclusiveBetween(BrainarrConstants.MinRecommendations, BrainarrConstants.MaxRecommendations)
                .WithMessage($"Recommendations must be between {BrainarrConstants.MinRecommendations} and {BrainarrConstants.MaxRecommendations}");

            // Validate the active provider settings polymorphically
            RuleFor(c => c.GetActiveProviderSettings())
                .Must(settings => settings.IsValid())
                .WithMessage("Provider settings are invalid")
                .When(c => c.GetActiveProviderSettings() != null);

            // More specific validation can be added here
            RuleFor(c => c)
                .Must(HaveValidProviderConfiguration)
                .WithMessage("Provider configuration is invalid");
        }

        private bool HaveValidProviderConfiguration(BrainarrSettingsRefactored settings)
        {
            var providerSettings = settings.GetActiveProviderSettings();
            return providerSettings != null && providerSettings.IsValid();
        }
    }
}