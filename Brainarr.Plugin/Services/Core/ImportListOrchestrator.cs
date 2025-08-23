using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Orchestrates the import list workflow with proper separation of concerns.
    /// Handles caching, health monitoring, rate limiting, and provider coordination.
    /// </summary>
    public class ImportListOrchestrator : IImportListOrchestrator
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IRecommendationCache _cache;
        private readonly IProviderHealthMonitor _healthMonitor;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IRateLimiter _rateLimiter;
        private readonly LibraryAwarePromptBuilder _promptBuilder;
        private readonly IterativeRecommendationStrategy _iterativeStrategy;
        private readonly BrainarrSettings _settings;
        private readonly Logger _logger;
        private IAIProvider _provider;
        private readonly int _definitionId;

        public ImportListOrchestrator(
            IArtistService artistService,
            IAlbumService albumService,
            IRecommendationCache cache,
            IProviderHealthMonitor healthMonitor,
            IRetryPolicy retryPolicy,
            IRateLimiter rateLimiter,
            LibraryAwarePromptBuilder promptBuilder,
            IterativeRecommendationStrategy iterativeStrategy,
            BrainarrSettings settings,
            int definitionId,
            Logger logger)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
            _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _iterativeStrategy = iterativeStrategy ?? throw new ArgumentNullException(nameof(iterativeStrategy));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _definitionId = definitionId;
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        public async Task<IList<ImportListItemInfo>> FetchRecommendationsAsync()
        {
            try
            {
                if (_provider == null)
                {
                    _logger.Error("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }

                // Get library profile for cache key
                var libraryProfile = GetLibraryProfile();
                var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);

                // Check cache first
                var cacheKey = _cache.GenerateCacheKey(
                    _settings.Provider.ToString(),
                    _settings.MaxRecommendations,
                    libraryFingerprint);

                if (_cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }

                // Check provider health
                var healthStatus = await _healthMonitor.CheckHealthAsync(
                    _settings.Provider.ToString(),
                    _settings.BaseUrl);

                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.Warn($"Provider {_settings.Provider} is unhealthy, returning empty list");
                    return new List<ImportListItemInfo>();
                }

                // Get library-aware recommendations using iterative strategy
                var startTime = DateTime.UtcNow;
                var recommendations = await _rateLimiter.ExecuteAsync(_settings.Provider.ToString().ToLower(), async () =>
                {
                    return await _retryPolicy.ExecuteAsync(
                        async () => await GetLibraryAwareRecommendationsAsync(libraryProfile),
                        $"GetRecommendations_{_settings.Provider}");
                });
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Record metrics
                _healthMonitor.RecordSuccess(_settings.Provider.ToString(), responseTime);

                if (!recommendations.Any())
                {
                    _logger.Warn("No recommendations received from AI provider");
                    return new List<ImportListItemInfo>();
                }

                // Convert to import items
                // Allow artist-only recommendations when in artist mode
                var shouldRecommendArtists = ShouldRecommendArtists();
                var uniqueItems = recommendations
                    .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && 
                               (shouldRecommendArtists || !string.IsNullOrWhiteSpace(r.Album)))
                    .Select(ConvertToImportItem)
                    .Where(item => item != null)
                    .ToList();

                // Cache the results
                _cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

                _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {_provider.ProviderName}");
                return uniqueItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                _healthMonitor.RecordFailure(_settings.Provider.ToString(), ex.Message);
                return new List<ImportListItemInfo>();
            }
        }

        public void InitializeProvider(IAIProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger.Info($"Provider initialized: {_provider.ProviderName}");
        }

        public LibraryProfile GetLibraryProfile()
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                var artistAlbumCounts = albums
                    .GroupBy(a => a.ArtistId)
                    .Select(g => new { ArtistId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(20)
                    .ToList();

                var topArtistNames = artistAlbumCounts
                    .Select(ac => artists.FirstOrDefault(a => a.Id == ac.ArtistId)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                var genreCounts = new Dictionary<string, int>();
                for (int i = 0; i < Math.Min(5, BrainarrConstants.FallbackGenres.Length); i++)
                {
                    genreCounts[BrainarrConstants.FallbackGenres[i]] = 20 - (i * 3);
                }

                return new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genreCounts,
                    TopArtists = topArtistNames,
                    RecentlyAdded = artists
                        .OrderByDescending(a => a.Added)
                        .Take(10)
                        .Select(a => a.Name)
                        .ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to get real library data, using sample: {ex.Message}");
                return GetFallbackLibraryProfile();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_provider == null)
            {
                _logger.Error("No provider configured for connection test");
                return false;
            }

            return await _provider.TestConnectionAsync();
        }

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(LibraryProfile profile)
        {
            try
            {
                var allArtists = _artistService.GetAllArtists();
                var allAlbums = _albumService.GetAllAlbums();

                _logger.Info($"Using library-aware strategy with {allArtists.Count} artists, {allAlbums.Count} albums");

                var recommendations = await _iterativeStrategy.GetIterativeRecommendationsAsync(
                    _provider, profile, allArtists, allAlbums, _settings);

                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(profile);
            }
        }

        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(LibraryProfile profile)
        {
            var prompt = BuildSimplePrompt(profile);
            return await _provider.GetRecommendationsAsync(prompt);
        }

        private string BuildSimplePrompt(LibraryProfile profile)
        {
            var prompt = $@"Based on this music library, recommend {_settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {_settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {GetDiscoveryFocus()}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";

            return prompt;
        }

        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();

            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
        }

        private string GetDiscoveryFocus()
        {
            return _settings.DiscoveryMode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }

        private bool ShouldRecommendArtists()
        {
            // Use the explicit user configuration
            var shouldRecommendArtists = _settings.RecommendationMode == RecommendationMode.Artists;
            
            if (_logger.IsDebugEnabled)
            {
                var mode = shouldRecommendArtists ? "Artists (All Albums)" : "Specific Albums";
                _logger.Debug($"Recommendation mode: {mode} (User setting: {_settings.RecommendationMode})");
            }
            
            return shouldRecommendArtists;
        }

        private ImportListItemInfo ConvertToImportItem(Recommendation rec)
        {
            try
            {
                // Validate artist is present
                if (string.IsNullOrWhiteSpace(rec.Artist))
                {
                    _logger.Debug($"Skipping recommendation with empty artist: '{rec.Artist}'");
                    return null;
                }

                // Clean the artist name
                var cleanArtist = rec.Artist?.Trim().Replace("\"", "").Replace("'", "'");
                
                // Handle artist-only vs album-specific recommendations
                string cleanAlbum;
                if (ShouldRecommendArtists() && string.IsNullOrWhiteSpace(rec.Album))
                {
                    // For artist recommendations, use a placeholder that tells Lidarr to import all albums
                    cleanAlbum = "[All Albums]";
                    _logger.Debug($"Converting artist-only recommendation: {cleanArtist} -> {cleanAlbum}");
                }
                else if (!string.IsNullOrWhiteSpace(rec.Album))
                {
                    // For album-specific recommendations, clean the album name
                    cleanAlbum = rec.Album.Trim().Replace("\"", "").Replace("'", "'");
                }
                else
                {
                    // Album mode but no album specified - invalid
                    _logger.Debug($"Skipping album-mode recommendation with empty album: '{rec.Artist}' - '{rec.Album}'");
                    return null;
                }

                return new ImportListItemInfo
                {
                    ImportListId = _definitionId,
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

        private LibraryProfile GetFallbackLibraryProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int>
                {
                    { "Rock", 30 }, { "Electronic", 20 }, { "Jazz", 15 }
                },
                TopArtists = new List<string>
                {
                    "Radiohead", "Pink Floyd", "Miles Davis"
                },
                RecentlyAdded = new List<string>()
            };
        }
    }
}