using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    /// <summary>
    /// Refactored Brainarr import list plugin for Lidarr
    /// Reduced from 711 lines to ~150 lines through proper separation of concerns
    /// </summary>
    public class BrainarrRefactored : ImportListBase<BrainarrSettings>
    {
        private readonly IImportListOrchestrator _orchestrator;
        private readonly IImportListUIHandler _uiHandler;
        private readonly IProviderLifecycleManager _providerManager;

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
            // Initialize core services following dependency injection patterns
            var modelDetection = new ModelDetectionService(httpClient, logger);
            var cache = new RecommendationCache(logger);
            var healthMonitor = new ProviderHealthMonitor(logger);
            var retryPolicy = new ExponentialBackoffRetryPolicy(logger);
            var rateLimiter = new RateLimiter(logger);
            var providerFactory = new AIProviderFactory();
            
            // Configure rate limiters
            RateLimiterConfiguration.ConfigureDefaults(rateLimiter);
            
            // Initialize library services
            var promptBuilder = new LibraryAwarePromptBuilder(logger);
            var iterativeStrategy = new IterativeRecommendationStrategy(logger, promptBuilder);
            
            // Create lifecycle manager for provider management
            _providerManager = new ProviderLifecycleManager(
                httpClient,
                providerFactory,
                modelDetection,
                healthMonitor,
                logger);
            
            // Create UI handler for model discovery and actions
            _uiHandler = new ImportListUIHandler(modelDetection, logger);
            
            // Create library context builder
            var libraryBuilder = new LibraryContextBuilder(artistService, albumService, logger);
            
            // Create orchestrator to coordinate everything
            _orchestrator = new ImportListOrchestrator(
                _providerManager,
                libraryBuilder,
                cache,
                rateLimiter,
                retryPolicy,
                promptBuilder,
                iterativeStrategy,
                artistService,
                albumService,
                modelDetection,
                logger,
                Definition?.Id ?? 0);
        }

        /// <summary>
        /// Fetches AI-powered music recommendations
        /// Delegates to orchestrator for clean separation of concerns
        /// </summary>
        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                // Delegate all fetching logic to the orchestrator
                var recommendations = _orchestrator.FetchRecommendationsAsync(Settings)
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

        /// <summary>
        /// Handles UI actions for model discovery and configuration
        /// Delegates to UI handler for clean separation of concerns
        /// </summary>
        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            _logger.Info($"RequestAction called with action: {action}");
            
            try
            {
                // Delegate all UI actions to the handler
                return _uiHandler.HandleAction(action, Settings, query);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = "An error occurred processing the request" };
            }
        }

        /// <summary>
        /// Tests the provider connection and configuration
        /// Delegates to orchestrator for validation
        /// </summary>
        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                // Delegate test logic to the orchestrator
                var testPassed = _orchestrator.TestConfigurationAsync(Settings, failures)
                    .GetAwaiter()
                    .GetResult();
                
                if (testPassed)
                {
                    _logger.Info("Configuration test passed successfully");
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }

        /// <summary>
        /// Cleanup resources on disposal
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _providerManager?.Dispose();
            }
            
            base.Dispose(disposing);
        }
    }
}