using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Advanced orchestrator implementation focused on intelligent recommendation generation
    /// with sophisticated library analysis and iterative refinement capabilities.
    /// This orchestrator provides more advanced features than the base BrainarrOrchestrator.
    /// </summary>
    /// <remarks>
    /// Key differentiators of this orchestrator:
    /// - Dependency injection for all supporting services (more testable)
    /// - Enhanced library-aware recommendation strategies
    /// - Advanced iterative refinement algorithms
    /// - Sophisticated caching strategies with library fingerprinting
    /// - Comprehensive health monitoring and failover capabilities
    /// 
    /// This orchestrator is designed for production environments where recommendation
    /// quality and system resilience are critical. It provides better separation of
    /// concerns and more sophisticated error handling than simpler implementations.
    /// </remarks>
    public class RecommendationOrchestrator : IRecommendationOrchestrator
    {
        private readonly IProviderFactory _providerFactory;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly ILibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private IAIProvider? _provider;

        /// <summary>
        /// Initializes a new instance of the RecommendationOrchestrator with full dependency injection.
        /// This constructor allows for maximum testability and flexibility in service composition.
        /// </summary>
        /// <param name="providerFactory">Factory for creating AI provider instances</param>
        /// <param name="cache">Caching service for recommendation results</param>
        /// <param name="healthMonitor">Health monitoring service for provider availability</param>
        /// <param name="retryPolicy">Retry policy for handling transient failures</param>
        /// <param name="rateLimiter">Rate limiting service for API calls</param>
        /// <param name="promptBuilder">Builder for creating library-aware prompts</param>
        /// <param name="iterativeStrategy">Strategy for iterative recommendation refinement</param>
        /// <param name="artistService">Lidarr artist service for library analysis</param>
        /// <param name="albumService">Lidarr album service for library profiling</param>
        /// <param name="httpClient">HTTP client for provider communications</param>
        /// <param name="logger">Logger instance for comprehensive monitoring</param>
        public RecommendationOrchestrator(
            IProviderFactory providerFactory,
            IRecommendationCache cache,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            ILibraryAwarePromptBuilder promptBuilder,
            IterativeRecommendationStrategy iterativeStrategy,
            IArtistService artistService,
            IAlbumService albumService,
            IHttpClient httpClient,
            Logger logger)
        {
            _providerFactory = providerFactory;
            _cache = cache;
            _healthMonitor = healthMonitor;
            _retryPolicy = retryPolicy;
            _rateLimiter = rateLimiter;
            _promptBuilder = promptBuilder;
            _iterativeStrategy = iterativeStrategy;
            _artistService = artistService;
            _albumService = albumService;
            _httpClient = httpClient;
            _logger = logger;
        }

        public void InitializeProvider(BrainarrSettings settings)
        {
            try
            {
                _provider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize provider: {settings.Provider}");
                _provider = null;
            }
        }

        public async Task<List<ImportListItemInfo>> GetRecommendationsAsync(
            BrainarrSettings settings, 
            LibraryProfile profile)
        {
            if (_provider == null)
            {
                InitializeProvider(settings);
                if (_provider == null)
                {
                    _logger.Error("Provider initialization failed");
                    return new List<ImportListItemInfo>();
                }
            }

            var libraryFingerprint = GenerateLibraryFingerprint(profile);
            var cacheKey = _cache.GenerateCacheKey(
                settings.Provider.ToString(), 
                settings.MaxRecommendations, 
                libraryFingerprint);
            
            if (_cache.TryGet(cacheKey, out var cachedRecommendations))
            {
                _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                return cachedRecommendations;
            }

            var healthStatus = await _healthMonitor.CheckHealthAsync(
                settings.Provider.ToString(), 
                settings.BaseUrl);
            
            if (healthStatus == HealthStatus.Unhealthy)
            {
                _logger.Warn($"Provider {settings.Provider} is unhealthy");
                return new List<ImportListItemInfo>();
            }
            
            var startTime = DateTime.UtcNow;
            var recommendations = await _rateLimiter.ExecuteAsync(
                settings.Provider.ToString().ToLower(), 
                async () => await _retryPolicy.ExecuteAsync(
                    async () => await GetLibraryAwareRecommendationsAsync(profile, settings),
                    $"GetRecommendations_{settings.Provider}"));
            
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _healthMonitor.RecordSuccess(settings.Provider.ToString(), responseTime);
            
            if (!recommendations.Any())
            {
                _logger.Warn("No recommendations received from AI provider");
                return new List<ImportListItemInfo>();
            }

            var uniqueItems = recommendations
                .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && !string.IsNullOrWhiteSpace(r.Album))
                .Select(ConvertToImportItem)
                .Where(item => item != null)
                .ToList();

            _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

            _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {_provider.ProviderName}");
            return uniqueItems;
        }

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            try
            {
                var allArtists = _artistService.GetAllArtists();
                var allAlbums = _albumService.GetAllAlbums();
                
                _logger.Info($"Using library-aware strategy with {allArtists.Count} artists, {allAlbums.Count} albums");
                
                var recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    _provider, profile, allArtists, allAlbums, settings);
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(profile, settings);
            }
        }
        
        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            var prompt = BuildSimplePrompt(profile, settings);
            return await _provider.GetRecommendationsAsync(prompt);
        }
        
        private string BuildSimplePrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            var prompt = $@"Based on this music library, recommend {settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {GetDiscoveryFocus(settings.DiscoveryMode)}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";

            return prompt;
        }

        private string GetDiscoveryFocus(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }

        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();
            
            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
        }

        private ImportListItemInfo ConvertToImportItem(Recommendation rec)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rec.Artist) || string.IsNullOrWhiteSpace(rec.Album))
                {
                    _logger.Debug($"Skipping recommendation with empty artist or album: '{rec.Artist}' - '{rec.Album}'");
                    return null;
                }

                var cleanArtist = rec.Artist?.Trim().Replace("\"", "").Replace("'", "'");
                var cleanAlbum = rec.Album?.Trim().Replace("\"", "").Replace("'", "'");

                return new ImportListItemInfo
                {
                    Artist = cleanArtist,
                    Album = cleanAlbum,
                    ArtistMusicBrainzId = null,
                    AlbumMusicBrainzId = null,
                    ReleaseDate = DateTime.UtcNow.AddDays(-30)
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to convert recommendation: {ex.Message}");
                return null;
            }
        }
    }
}