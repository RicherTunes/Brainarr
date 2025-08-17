using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class RecommendationService : IRecommendationService
    {
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly IRecommendationSanitizer _sanitizer;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly Logger _logger;

        public RecommendationService(
            IRecommendationCache cache,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            IRecommendationSanitizer sanitizer,
            IterativeRecommendationStrategy iterativeStrategy,
            Logger logger)
        {
            _cache = cache;
            _healthMonitor = healthMonitor;
            _retryPolicy = retryPolicy;
            _rateLimiter = rateLimiter;
            _sanitizer = sanitizer;
            _iterativeStrategy = iterativeStrategy;
            _logger = logger;
        }

        public async Task<IList<ImportListItemInfo>> GetRecommendationsAsync(
            string provider,
            int maxRecommendations,
            string libraryFingerprint)
        {
            var cacheKey = _cache.GenerateCacheKey(provider, maxRecommendations, libraryFingerprint);
            
            if (_cache.TryGet(cacheKey, out var cachedRecommendations))
            {
                _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                return cachedRecommendations;
            }

            return new List<ImportListItemInfo>();
        }

        public async Task<IList<ImportListItemInfo>> GenerateRecommendationsAsync(
            IAIProvider provider,
            int maxRecommendations,
            LibraryProfile libraryProfile)
        {
            try
            {
                await _rateLimiter.WaitAsync(provider.Name);

                var recommendations = await _retryPolicy.ExecuteAsync(async () =>
                {
                    var result = await _iterativeStrategy.GetRecommendationsAsync(
                        provider,
                        libraryProfile,
                        maxRecommendations);
                    
                    return _sanitizer.SanitizeRecommendations(result, libraryProfile);
                });

                _logger.Info($"Generated {recommendations.Count} recommendations from {provider.Name}");
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to generate recommendations from {provider.Name}");
                await _healthMonitor.RecordFailureAsync(provider.Name);
                throw;
            }
        }
    }
}