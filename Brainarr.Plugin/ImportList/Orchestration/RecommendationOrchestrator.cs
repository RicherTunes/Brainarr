using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Plugin.ImportList.Orchestration
{
    /// <summary>
    /// Orchestrates the recommendation fetching process
    /// </summary>
    public class RecommendationOrchestrator
    {
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly Logger _logger;

        public RecommendationOrchestrator(
            IRecommendationCache cache,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            Logger logger)
        {
            _cache = cache;
            _healthMonitor = healthMonitor;
            _retryPolicy = retryPolicy;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        /// <summary>
        /// Orchestrates the recommendation fetch process with caching and health checks
        /// </summary>
        public async Task<List<Recommendation>> GetRecommendationsAsync(
            IAIProvider provider,
            BrainarrSettings settings,
            LibraryProfile libraryProfile)
        {
            if (provider == null)
            {
                _logger.Error("No AI provider configured");
                return new List<Recommendation>();
            }

            // Generate cache key
            var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
            var cacheKey = _cache.GenerateCacheKey(
                settings.Provider.ToString(),
                settings.MaxRecommendations,
                libraryFingerprint);

            // Check cache
            if (_cache.TryGet(cacheKey, out var cachedRecommendations))
            {
                _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                return cachedRecommendations;
            }

            // Health check
            var healthStatus = await _healthMonitor.CheckHealthAsync(
                settings.Provider.ToString(),
                settings.BaseUrl);

            if (healthStatus == HealthStatus.Unhealthy)
            {
                _logger.Warn($"Provider {settings.Provider} is unhealthy");
                return new List<Recommendation>();
            }

            // Fetch with rate limiting and retry
            var startTime = DateTime.UtcNow;
            var recommendations = await ExecuteWithPoliciesAsync(provider, libraryProfile, settings);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // Record metrics
            _healthMonitor.RecordSuccess(settings.Provider.ToString(), responseTime);

            // Cache results
            if (recommendations.Any())
            {
                _cache.Set(cacheKey, recommendations, TimeSpan.FromMinutes(settings.CacheDurationMinutes));
                _logger.Info($"Fetched {recommendations.Count} recommendations from {provider.ProviderName}");
            }

            return recommendations;
        }

        private async Task<List<Recommendation>> ExecuteWithPoliciesAsync(
            IAIProvider provider,
            LibraryProfile libraryProfile,
            BrainarrSettings settings)
        {
            return await _rateLimiter.ExecuteAsync(
                settings.Provider.ToString().ToLower(),
                async () =>
                {
                    return await _retryPolicy.ExecuteAsync(
                        async () => await provider.GetRecommendationsAsync(libraryProfile, settings),
                        $"GetRecommendations_{settings.Provider}");
                });
        }

        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            // Generate a fingerprint based on library characteristics
            var components = new[]
            {
                profile.TotalArtists.ToString(),
                profile.TotalAlbums.ToString(),
                string.Join(",", profile.TopGenres.Take(5)),
                string.Join(",", profile.TopArtists.Take(10))
            };

            var combined = string.Join("|", components);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(combined);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}