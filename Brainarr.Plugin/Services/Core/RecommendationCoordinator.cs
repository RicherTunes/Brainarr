using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly IRecommendationCacheKeyBuilder _keyBuilder;

        // lightweight profile cache
        private readonly object _profileLock = new object();
        private LibraryProfile _cachedProfile;
        private DateTime _cachedAt = DateTime.MinValue;
        private static readonly TimeSpan ProfileTtl = TimeSpan.FromMinutes(10);

        public RecommendationCoordinator(
            Logger logger,
            IRecommendationCache cache,
            IRecommendationPipeline pipeline,
            IRecommendationSanitizer sanitizer,
            IRecommendationSchemaValidator schemaValidator,
            RecommendationHistory history,
            ILibraryAnalyzer libraryAnalyzer,
            IRecommendationCacheKeyBuilder keyBuilder)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
            _keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
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
            // Compute or get cached library profile
            var libraryProfile = GetLibraryProfileWithCache();
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

        private LibraryProfile GetLibraryProfileWithCache()
        {
            lock (_profileLock)
            {
                if (_cachedProfile != null && (DateTime.UtcNow - _cachedAt) < ProfileTtl)
                {
                    return _cachedProfile;
                }
            }

            var profile = _libraryAnalyzer.AnalyzeLibrary();
            lock (_profileLock)
            {
                _cachedProfile = profile;
                _cachedAt = DateTime.UtcNow;
            }
            return profile;
        }

        // Key building now delegated to IRecommendationCacheKeyBuilder
    }
}
