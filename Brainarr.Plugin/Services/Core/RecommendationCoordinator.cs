using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Utilities;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Parser.Model;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class RecommendationCoordinator : IRecommendationCoordinator
    {
        private readonly Logger _logger;
        private readonly IRecommendationCache _cache;
        private readonly IRecommendationPipeline _pipeline;
        private readonly IRecommendationSanitizer _sanitizer;
        private readonly IRecommendationSchemaValidator _schemaValidator;
        private readonly RecommendationHistory _history;
        private readonly ILibraryProfileService _profileService;
        private readonly IRecommendationCacheKeyBuilder _keyBuilder;

        // Profile caching moved to ILibraryProfileService

        public RecommendationCoordinator(
            Logger logger,
            IRecommendationCache cache,
            IRecommendationPipeline pipeline,
            IRecommendationSanitizer sanitizer,
            IRecommendationSchemaValidator schemaValidator,
            RecommendationHistory history,
            ILibraryProfileService profileService,
            IRecommendationCacheKeyBuilder keyBuilder)
        {
            _logger = Guard.NotNull(logger);
            _cache = Guard.NotNull(cache);
            _pipeline = Guard.NotNull(pipeline);
            _sanitizer = Guard.NotNull(sanitizer);
            _schemaValidator = Guard.NotNull(schemaValidator);
            _history = Guard.NotNull(history);
            _profileService = Guard.NotNull(profileService);
            _keyBuilder = Guard.NotNull(keyBuilder);
        }

        public async Task<List<ImportListItemInfo>> RunAsync(
            BrainarrSettings settings,
            Func<LibraryProfile, CancellationToken, Task<List<Recommendation>>> fetchRecommendations,
            ReviewQueueService reviewQueue,
            IAIProvider currentProvider,
            ILibraryAwarePromptBuilder promptBuilder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Compute or get current library profile from service
            var libraryProfile = _profileService.GetLibraryProfile();
            var cacheKey = _keyBuilder.Build(settings, libraryProfile);

            // Cache check
            if (_cache.TryGet(cacheKey, out var cached))
            {
                _logger.Debug($"RecommendationCoordinator: cache hit for {cacheKey}");
                return cached;
            }

            // Fetch raw recs from provider
            var recs = await fetchRecommendations(libraryProfile, cancellationToken).ConfigureAwait(false);

            // Sanitize + schema validation
            var sanitized = _sanitizer.SanitizeRecommendations(recs);
            var schemaReport = _schemaValidator.Validate(sanitized);
            try
            {
                NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.EventLogger.Log(_logger,
                    NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.BrainarrEvent.SanitizationComplete,
                    $"items={schemaReport.TotalItems} dropped={schemaReport.DroppedItems} clamped={schemaReport.ClampedConfidences} trimmed={schemaReport.TrimmedFields}");
            }
            catch { }

            _history.RecordSuggestions(sanitized);

            // Pipeline (validate→enrich→gates→convert→dedupe→top-up)
            var importItems = await _pipeline.ProcessAsync(
                settings,
                sanitized,
                libraryProfile,
                reviewQueue,
                currentProvider,
                promptBuilder,
                cancellationToken).ConfigureAwait(false);

            // Cache store
            _cache.Set(cacheKey, importItems, settings.CacheDuration);
            return importItems;
        }

        // Profile caching is handled by ILibraryProfileService

        // Key building now delegated to IRecommendationCacheKeyBuilder
    }
}
