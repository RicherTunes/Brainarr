using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
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
    public class BrainarrRefactored : ImportListBase<BrainarrSettings>
    {
        private readonly IModelActionHandler _modelActionHandler;
        private readonly IRecommendationOrchestrator _recommendationOrchestrator;
        private readonly ILibraryContextBuilder _libraryContextBuilder;
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
            
            // Initialize core services with dependency injection
            var modelDetection = new ModelDetectionService(httpClient, logger);
            var cache = new RecommendationCache(logger);
            var healthMonitor = new ProviderHealthMonitor(logger);
            var retryPolicy = new ExponentialBackoffRetryPolicy(logger);
            var rateLimiter = new RateLimiter(logger);
            var providerFactory = new AIProviderFactory();
            var promptBuilder = new LibraryAwarePromptBuilder(logger);
            var iterativeStrategy = new IterativeRecommendationStrategy(logger, promptBuilder);
            
            // Configure rate limiters
            RateLimiterConfiguration.ConfigureDefaults(rateLimiter);
            
            // Initialize decomposed services
            _modelActionHandler = new ModelActionHandler(
                modelDetection, 
                providerFactory, 
                httpClient, 
                logger);
                
            _recommendationOrchestrator = new RecommendationOrchestrator(
                providerFactory,
                cache,
                healthMonitor,
                retryPolicy,
                rateLimiter,
                promptBuilder,
                iterativeStrategy,
                artistService,
                albumService,
                httpClient,
                logger);
                
            _libraryContextBuilder = new LibraryContextBuilder(logger);
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                // Build library profile
                var libraryProfile = _libraryContextBuilder.BuildProfile(_artistService, _albumService);
                
                // Initialize provider and get recommendations
                _recommendationOrchestrator.InitializeProvider(Settings);
                var recommendations = _recommendationOrchestrator
                    .GetRecommendationsAsync(Settings, libraryProfile)
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
            _logger.Info($"RequestAction called with action: {action}");
            return _modelActionHandler.HandleProviderAction(action, Settings);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                var result = _modelActionHandler
                    .HandleTestConnectionAsync(Settings)
                    .GetAwaiter()
                    .GetResult();
                
                if (result.StartsWith("Failed"))
                {
                    failures.Add(new ValidationFailure(string.Empty, result));
                }
                else
                {
                    _logger.Info($"Test successful: {result}");
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Test failed: {ex.Message}"));
            }
        }
    }
}