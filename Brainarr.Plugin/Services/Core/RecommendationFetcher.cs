using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class RecommendationFetcher : IRecommendationFetcher
    {
        private readonly IServiceConfiguration _services;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public RecommendationFetcher(
            IServiceConfiguration services,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings, int definitionId)
        {
            try
            {
                var provider = InitializeProvider(settings);
                if (provider == null)
                {
                    _logger.Error("No AI provider configured");
                    return new List<ImportListItemInfo>();
                }

                var libraryProfile = GetRealLibraryProfile();
                var libraryFingerprint = GenerateLibraryFingerprint(libraryProfile);
                
                var cacheKey = _services.Cache.GenerateCacheKey(
                    settings.Provider.ToString(), 
                    settings.MaxRecommendations, 
                    libraryFingerprint);
                
                if (_services.Cache.TryGet(cacheKey, out var cachedRecommendations))
                {
                    _logger.Info($"Returning {cachedRecommendations.Count} cached recommendations");
                    return cachedRecommendations;
                }

                var healthStatus = _services.HealthMonitor.CheckHealthAsync(
                        settings.Provider.ToString(), 
                        settings.BaseUrl).GetAwaiter().GetResult();
                
                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.Warn($"Provider {settings.Provider} is unhealthy, returning empty list");
                    return new List<ImportListItemInfo>();
                }
                
                var startTime = DateTime.UtcNow;
                var recommendations = _services.RateLimiter.ExecuteAsync(
                    settings.Provider.ToString().ToLower(), 
                    async () =>
                    {
                        return await _services.RetryPolicy.ExecuteAsync(
                            async () => await GetLibraryAwareRecommendationsAsync(provider, libraryProfile, settings),
                            $"GetRecommendations_{settings.Provider}");
                    }).GetAwaiter().GetResult();
                    
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                _services.HealthMonitor.RecordSuccess(settings.Provider.ToString(), responseTime);
                
                if (!recommendations.Any())
                {
                    _logger.Warn("No recommendations received from AI provider");
                    return new List<ImportListItemInfo>();
                }

                var uniqueItems = recommendations
                    .Where(r => !string.IsNullOrWhiteSpace(r.Artist) && !string.IsNullOrWhiteSpace(r.Album))
                    .Select(r => ConvertToImportItem(r, definitionId))
                    .Where(item => item != null)
                    .ToList();

                _services.Cache.Set(cacheKey, uniqueItems, TimeSpan.FromMinutes(BrainarrConstants.CacheDurationMinutes));

                _logger.Info($"Fetched {uniqueItems.Count} unique recommendations from {provider.ProviderName}");
                return uniqueItems;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error fetching AI recommendations");
                _services.HealthMonitor.RecordFailure(settings.Provider.ToString(), ex.Message);
                return new List<ImportListItemInfo>();
            }
        }

        private IAIProvider InitializeProvider(BrainarrSettings settings)
        {
            if (settings.AutoDetectModel)
            {
                AutoDetectAndSetModel(settings);
            }
            
            return _services.CreateProvider(settings);
        }

        private void AutoDetectAndSetModel(BrainarrSettings settings)
        {
            try
            {
                _logger.Info($"Auto-detecting models for {settings.Provider}");
                
                List<string> detectedModels;
                if (settings.Provider == AIProvider.Ollama)
                {
                    detectedModels = _services.ModelDetection.GetOllamaModelsAsync(settings.OllamaUrl)
                        .GetAwaiter().GetResult();
                }
                else if (settings.Provider == AIProvider.LMStudio)
                {
                    detectedModels = _services.ModelDetection.GetLMStudioModelsAsync(settings.LMStudioUrl)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    detectedModels = new List<string>();
                }
                
                if (detectedModels != null && detectedModels.Any())
                {
                    var preferredModels = new[] { "qwen", "llama", "mistral", "phi", "gemma" };
                    
                    string selectedModel = null;
                    foreach (var preferred in preferredModels)
                    {
                        selectedModel = detectedModels.FirstOrDefault(m => m.ToLower().Contains(preferred));
                        if (selectedModel != null) break;
                    }
                    
                    selectedModel = selectedModel ?? detectedModels.First();
                    
                    if (settings.Provider == AIProvider.Ollama)
                    {
                        settings.OllamaModel = selectedModel;
                        _logger.Info($"Auto-detected Ollama model: {selectedModel}");
                    }
                    else
                    {
                        settings.LMStudioModel = selectedModel;
                        _logger.Info($"Auto-detected LM Studio model: {selectedModel}");
                    }
                }
                else
                {
                    _logger.Warn($"No models detected for {settings.Provider}, using configured default");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to auto-detect models for {settings.Provider}, using configured default");
            }
        }

        private LibraryProfile GetRealLibraryProfile()
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

        private async Task<List<Recommendation>> GetLibraryAwareRecommendationsAsync(
            IAIProvider provider, 
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            try
            {
                var allArtists = _artistService.GetAllArtists();
                var allAlbums = _albumService.GetAllAlbums();
                
                _logger.Info($"Using library-aware strategy with {allArtists.Count} artists, {allAlbums.Count} albums");
                
                var recommendations = await _services.IterativeStrategy.GetIterativeRecommendationsAsync(
                    provider, profile, allArtists, allAlbums, settings);
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Library-aware recommendation failed, falling back to simple prompt");
                return await GetSimpleRecommendationsAsync(provider, profile, settings);
            }
        }
        
        private async Task<List<Recommendation>> GetSimpleRecommendationsAsync(
            IAIProvider provider, 
            LibraryProfile profile, 
            BrainarrSettings settings)
        {
            var prompt = BuildSimplePrompt(profile, settings);
            return await provider.GetRecommendationsAsync(prompt);
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
        
        private string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();
            
            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
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

        private ImportListItemInfo ConvertToImportItem(Recommendation rec, int definitionId)
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
                    ImportListId = definitionId,
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

    public interface IRecommendationFetcher
    {
        IList<ImportListItemInfo> FetchRecommendations(BrainarrSettings settings, int definitionId);
    }
}