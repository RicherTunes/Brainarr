using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    /// <summary>
    /// Refactored Brainarr import list with proper separation of concerns.
    /// Delegates orchestration to ImportListOrchestrator and action handling to ImportListActionHandler.
    /// </summary>
    public class BrainarrRefactored : ImportListBase<BrainarrSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IImportListOrchestrator _orchestrator;
        private readonly ImportListActionHandler _actionHandler;
        private readonly IProviderFactory _providerFactory;
        private readonly ModelDetectionService _modelDetection;

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
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Initialize core services
            var cache = new RecommendationCache(logger);
            var healthMonitor = new ProviderHealthMonitor(logger);
            var retryPolicy = new ExponentialBackoffRetryPolicy(logger);
            var rateLimiter = new RateLimiter(logger);
            var promptBuilder = new LibraryAwarePromptBuilder(logger);
            var iterativeStrategy = new IterativeRecommendationStrategy(logger, promptBuilder);

            // Configure rate limiting
            RateLimiterConfiguration.ConfigureDefaults(rateLimiter);

            // Initialize model detection
            _modelDetection = new ModelDetectionService(httpClient, logger);

            // Initialize orchestrator
            _orchestrator = new ImportListOrchestrator(
                artistService,
                albumService,
                cache,
                healthMonitor,
                retryPolicy,
                rateLimiter,
                promptBuilder,
                iterativeStrategy,
                Settings,
                Definition.Id,
                logger);

            // Initialize action handler
            _actionHandler = new ImportListActionHandler(
                _modelDetection,
                Settings,
                logger);

            // Initialize provider factory
            _providerFactory = new AIProviderFactory();
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                // Initialize provider with auto-detection if enabled
                InitializeProvider();

                // Delegate to orchestrator for the main workflow
                var recommendations = _orchestrator.FetchRecommendationsAsync()
                    .GetAwaiter()
                    .GetResult();

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
            // Delegate UI actions to the action handler
            return _actionHandler.HandleAction(action, query);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                InitializeProvider();

                var connected = _orchestrator.TestConnectionAsync()
                    .GetAwaiter()
                    .GetResult();

                if (!connected)
                {
                    failures.Add(new ValidationFailure(string.Empty,
                        $"Cannot connect to {Settings.Provider}"));
                    return;
                }

                // Try to detect available models for local providers
                if (Settings.Provider == AIProvider.Ollama || Settings.Provider == AIProvider.LMStudio)
                {
                    TestLocalProviderModels(failures);
                }
                else
                {
                    _logger.Info($"✅ Connected successfully to {Settings.Provider}");
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        private void InitializeProvider()
        {
            // Auto-detect models if enabled
            if (Settings.AutoDetectModel)
            {
                _actionHandler.AutoDetectAndSetModelAsync()
                    .GetAwaiter()
                    .GetResult();
            }

            // Create provider using factory
            try
            {
                var provider = _providerFactory.CreateProvider(Settings, _httpClient, _logger);
                _orchestrator.InitializeProvider(provider);
            }
            catch (NotSupportedException ex)
            {
                _logger.Error(ex, $"Provider type {Settings.Provider} is not supported");
            }
            catch (ArgumentException ex)
            {
                _logger.Error(ex, "Invalid provider configuration");
            }
        }

        private void TestLocalProviderModels(List<ValidationFailure> failures)
        {
            try
            {
                List<string> models = null;
                string providerName = "";

                if (Settings.Provider == AIProvider.Ollama)
                {
                    models = _modelDetection.GetOllamaModelsAsync(Settings.OllamaUrl)
                        .GetAwaiter()
                        .GetResult();
                    providerName = "Ollama";
                }
                else if (Settings.Provider == AIProvider.LMStudio)
                {
                    models = _modelDetection.GetLMStudioModelsAsync(Settings.LMStudioUrl)
                        .GetAwaiter()
                        .GetResult();
                    providerName = "LM Studio";
                }

                if (models?.Any() == true)
                {
                    _logger.Info($"✅ Found {models.Count} {providerName} models: {string.Join(", ", models)}");
                    Settings.DetectedModels = models;

                    var topModels = models.Take(3).ToList();
                    var modelList = string.Join(", ", topModels);
                    if (models.Count > 3) modelList += $" (and {models.Count - 3} more)";

                    _logger.Info($"🎯 Recommended: Copy one of these models into the field above: {modelList}");
                }
                else
                {
                    var errorMessage = Settings.Provider == AIProvider.Ollama
                        ? "No suitable models found. Install models like: ollama pull qwen2.5"
                        : "No models loaded. Load a model in LM Studio first.";

                    failures.Add(new ValidationFailure(string.Empty, errorMessage));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to detect models for {Settings.Provider}");
            }
        }
    }
}