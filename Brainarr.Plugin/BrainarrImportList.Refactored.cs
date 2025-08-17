using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public class BrainarrRefactored : ImportListBase<BrainarrSettings>
    {
        private readonly ServiceContainer _serviceContainer;
        private readonly IProviderInitializationService _providerInit;
        private readonly ILibraryProfileService _libraryProfileService;
        private readonly IRecommendationService _recommendationService;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;

        public override string Name => "Brainarr AI Music Discovery";
        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public BrainarrRefactored(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger) : base(importListStatusService, configService, parsingService, logger)
        {
            _artistService = artistService;
            _albumService = albumService;

            _serviceContainer = new ServiceContainer(
                httpClient,
                artistService,
                albumService,
                configService,
                parsingService,
                logger);

            _providerInit = _serviceContainer.GetRequiredService<IProviderInitializationService>();
            _libraryProfileService = _serviceContainer.GetRequiredService<ILibraryProfileService>();
            _recommendationService = _serviceContainer.GetRequiredService<IRecommendationService>();
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                var provider = _providerInit.InitializeProviderAsync(Settings).GetAwaiter().GetResult();
                
                if (provider == null)
                {
                    _logger.Error("Failed to initialize AI provider");
                    return new List<ImportListItemInfo>();
                }

                var libraryProfile = _libraryProfileService.GetEnhancedLibraryProfile(
                    _artistService,
                    _albumService,
                    Settings);

                var libraryFingerprint = _libraryProfileService.GenerateLibraryFingerprint(libraryProfile);

                var cachedRecommendations = _recommendationService.GetRecommendationsAsync(
                    Settings.Provider.ToString(),
                    Settings.MaxRecommendations,
                    libraryFingerprint).GetAwaiter().GetResult();

                if (cachedRecommendations?.Any() == true)
                {
                    return cachedRecommendations;
                }

                var recommendations = _recommendationService.GenerateRecommendationsAsync(
                    provider,
                    Settings.MaxRecommendations,
                    libraryProfile).GetAwaiter().GetResult();

                _logger.Info($"Fetched {recommendations.Count} recommendations from {provider.Name}");
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                return new List<ImportListItemInfo>();
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getModelOptions")
            {
                var modelDetection = _serviceContainer.GetRequiredService<IModelDetectionService>();
                return GetModelOptions(modelDetection);
            }

            return new { };
        }

        private object GetModelOptions(IModelDetectionService modelDetection)
        {
            return Settings.Provider switch
            {
                AIProvider.Ollama => GetDynamicModelOptions(
                    modelDetection,
                    Settings.OllamaUrl,
                    provider => modelDetection.DetectAvailableModelsAsync(Settings.OllamaUrl, provider)),
                
                AIProvider.LMStudio => GetDynamicModelOptions(
                    modelDetection,
                    Settings.LMStudioUrl,
                    provider => modelDetection.DetectAvailableModelsAsync(Settings.LMStudioUrl, provider)),
                
                _ => GetStaticModelOptions(Settings.Provider)
            };
        }

        private async Task<object> GetDynamicModelOptions(
            IModelDetectionService modelDetection,
            string url,
            Func<AIProvider, Task<List<string>>> detectFunc)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return GetFallbackOptions(Settings.Provider);
            }

            try
            {
                var models = await detectFunc(Settings.Provider);
                if (models?.Any() == true)
                {
                    return new
                    {
                        options = models.Select(m => new
                        {
                            Value = m,
                            Name = FormatModelName(m)
                        })
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to get {Settings.Provider} models");
            }

            return GetFallbackOptions(Settings.Provider);
        }

        private object GetStaticModelOptions(AIProvider provider)
        {
            var enumType = provider switch
            {
                AIProvider.OpenAI => typeof(OpenAIModel),
                AIProvider.Anthropic => typeof(AnthropicModel),
                AIProvider.Gemini => typeof(GeminiModel),
                _ => null
            };

            if (enumType == null)
            {
                return new { options = new List<object>() };
            }

            return new
            {
                options = Enum.GetValues(enumType)
                    .Cast<Enum>()
                    .Select(e => new
                    {
                        Value = e.ToString(),
                        Name = FormatEnumName(e.ToString())
                    })
            };
        }

        private object GetFallbackOptions(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama => new
                {
                    options = new[]
                    {
                        new { Value = "qwen2.5:latest", Name = "Qwen 2.5 (Recommended)" },
                        new { Value = "llama3.2:latest", Name = "Llama 3.2" }
                    }
                },
                AIProvider.LMStudio => new
                {
                    options = new[]
                    {
                        new { Value = "local-model", Name = "Currently Loaded Model" }
                    }
                },
                _ => new { options = new List<object>() }
            };
        }

        private string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return "Unknown Model";
            
            if (modelId.Contains("/"))
            {
                var parts = modelId.Split('/');
                if (parts.Length >= 2)
                {
                    return $"{CleanModelName(parts[1])} ({parts[0]})";
                }
            }
            
            if (modelId.Contains(":"))
            {
                var parts = modelId.Split(':');
                if (parts.Length >= 2)
                {
                    return $"{CleanModelName(parts[0])}:{parts[1]}";
                }
            }
            
            return CleanModelName(modelId);
        }

        private string CleanModelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            return name
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace("qwen", "Qwen", StringComparison.OrdinalIgnoreCase)
                .Replace("llama", "Llama", StringComparison.OrdinalIgnoreCase)
                .Replace("mistral", "Mistral", StringComparison.OrdinalIgnoreCase);
        }

        private string FormatEnumName(string enumValue)
        {
            return enumValue
                .Replace("_", " ")
                .Replace("GPT4o", "GPT-4o")
                .Replace("Claude35", "Claude 3.5");
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                var provider = _providerInit.InitializeProviderAsync(Settings).GetAwaiter().GetResult();
                
                if (provider == null)
                {
                    failures.Add(new ValidationFailure(nameof(Settings.Provider),
                        "Failed to initialize AI provider"));
                    return;
                }

                var isValid = _providerInit.ValidateProviderAsync(provider, Settings).GetAwaiter().GetResult();
                
                if (!isValid)
                {
                    failures.Add(new ValidationFailure(string.Empty,
                        $"Provider {Settings.Provider} validation failed"));
                    return;
                }

                _logger.Info($"Test successful: Connected to {provider.Name}");
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _serviceContainer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}