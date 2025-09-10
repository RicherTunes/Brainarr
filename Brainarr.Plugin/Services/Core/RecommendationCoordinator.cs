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
            ILibraryAnalyzer libraryAnalyzer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _libraryAnalyzer = libraryAnalyzer ?? throw new ArgumentNullException(nameof(libraryAnalyzer));
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
            var cacheKey = GenerateCacheKey(settings, libraryProfile);

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

        private static string GenerateCacheKey(BrainarrSettings settings, LibraryProfile profile)
        {
            var topGenres = profile?.TopGenres != null
                ? string.Join(",", profile.TopGenres.Keys.OrderBy(k => k).Take(5))
                : "";
            var topArtists = profile?.TopArtists != null
                ? string.Join(",", profile.TopArtists.OrderBy(a => a).Take(5))
                : "";
            var effectiveModel = settings.EffectiveModel ?? settings.ModelSelection ?? string.Empty;
            var raw = string.Join("|", new[]
            {
                $"cache_v={Configuration.BrainarrConstants.CacheKeyVersion}",
                $"san_v={Configuration.BrainarrConstants.SanitizerVersion}",
                $"provider={settings.Provider}",
                $"mode={settings.DiscoveryMode}",
                $"recmode={settings.RecommendationMode}",
                $"sampling={settings.SamplingStrategy}",
                $"model={effectiveModel}",
                $"max={settings.MaxRecommendations}",
                $"genres={topGenres}",
                $"artists={topArtists}"
            });

            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            var hash = Convert.ToBase64String(sha.ComputeHash(bytes))
                .Replace("/", "_")
                .Replace("+", "-");
            return $"rec_{hash.Substring(0, 24)}";
        }
    }
}
