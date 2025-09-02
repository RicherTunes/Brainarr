using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
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
    /// Main Lidarr import list implementation for AI-powered music discovery.
    /// Integrates multiple AI providers to generate intelligent music recommendations
    /// based on the user's existing library.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Multi-provider support with automatic failover
    /// - Intelligent caching to reduce API calls
    /// - Library-aware prompts for personalized recommendations
    /// - Health monitoring and rate limiting
    /// - Iterative recommendation strategy for quality results
    /// 
    /// The plugin follows Lidarr's import list pattern, fetching recommendations
    /// periodically and converting them to ImportListItemInfo objects that
    /// Lidarr can process for automatic album additions.
    /// </remarks>
    public class Brainarr : ImportListBase<BrainarrSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IBrainarrOrchestrator _orchestrator;

        public override string Name => "Brainarr AI Music Discovery";
        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);

        public Brainarr(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger,
            IBrainarrOrchestrator? orchestrator = null) : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            
            // Use orchestrator directly - can be injected for testing or falls back to default implementation
            if (orchestrator != null)
            {
                _orchestrator = orchestrator;
            }
            else
            {
                // Create default orchestrator with required dependencies
                var providerFactory = new AIProviderFactory();
                var libraryAnalyzer = new LibraryAnalyzer(artistService, albumService, logger);
                var cache = new RecommendationCache(logger);
                var healthMonitor = new ProviderHealthMonitor(logger);
                var validator = new RecommendationValidator(logger);
                var modelDetection = new ModelDetectionService(httpClient, logger);
                var duplicationPrevention = new Services.DuplicationPreventionService(logger);
                
                _orchestrator = new BrainarrOrchestrator(
                    logger,
                    providerFactory,
                    libraryAnalyzer,
                    cache,
                    healthMonitor,
                    validator,
                    modelDetection,
                    httpClient,
                    duplicationPrevention);
            }
        }

        /// <summary>
        /// Fetches AI-generated music recommendations based on the user's library.
        /// </summary>
        /// <returns>List of recommended albums as ImportListItemInfo objects</returns>
        /// <remarks>
        /// Execution flow:
        /// 1. Initialize/validate AI provider configuration
        /// 2. Check cache for recent recommendations
        /// 3. Build library profile from existing music
        /// 4. Generate context-aware prompt
        /// 5. Get recommendations using iterative strategy
        /// 6. Validate and sanitize results
        /// 7. Cache successful recommendations
        /// 8. Convert to Lidarr import format
        /// 
        /// IMPORTANT: This method is required to be synchronous by Lidarr's ImportListBase,
        /// but our implementation is async. We use AsyncHelper to safely bridge this gap
        /// without risking deadlocks.
        /// </remarks>
        public override IList<ImportListItemInfo> Fetch()
        {
            // Delegate to the advanced orchestrator which handles all sophisticated features:
            // - Correlation tracking, health monitoring, rate limiting
            // - Library-aware recommendations, iterative refinement
            // - Automatic model detection, fallback handling
            // - Comprehensive caching and error handling
            var items = _orchestrator.FetchRecommendations(Settings);
            // Ensure ImportListId/ImportList fields are populated for Lidarr processing
            return CleanupListItems(items);
        }

        /// <summary>
        /// Validates the plugin configuration by testing the connection to the AI provider.
        /// Delegates validation to the orchestrator which has comprehensive provider testing.
        /// </summary>
        protected override void Test(List<ValidationFailure> failures)
        {
            // Delegate to the orchestrator's validation logic
            _orchestrator.ValidateConfiguration(Settings, failures);
        }

        /// <summary>
        /// Public wrapper for testing configuration. This allows unit tests to validate configuration.
        /// </summary>
        /// <param name="failures">List to collect validation failures</param>
        public void TestConfiguration(List<ValidationFailure> failures)
        {
            Test(failures);
        }


        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            // Delegate all UI actions to the orchestrator's action handler
            return _orchestrator.HandleAction(action, query, Settings);
        }

    }

}
