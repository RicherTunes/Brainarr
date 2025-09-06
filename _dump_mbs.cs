using System;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using System.Collections.Concurrent;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Validation
{
    /// <summary>
    /// Interface for MusicBrainz music database integration.
    /// </summary>
    public interface IMusicBrainzService
    {
        Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle);
        Task<bool> ValidateArtistAsync(string artistName);
        Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle);

        // Cancellation-aware overloads
        Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle, System.Threading.CancellationToken cancellationToken);
        Task<bool> ValidateArtistAsync(string artistName, System.Threading.CancellationToken cancellationToken);
        Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle, System.Threading.CancellationToken cancellationToken);
    }

    /// <summary>
    /// MusicBrainz service for validating music recommendations against the open music database.
    /// </summary>
    public class MusicBrainzService : IMusicBrainzService
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter _rateLimiter;
        
        // In-process caches (provider-agnostic)
        private readonly ConcurrentDictionary<string, CacheEntry<bool>> _artistExistsCache = new();
        private readonly ConcurrentDictionary<string, CacheEntry<MusicBrainzSearchResult?>> _artistAlbumCache = new();
        private static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(2);
        private const int MaxEntries = 1000;
        
        private const string MUSICBRAINZ_BASE_URL = "https://musicbrainz.org/ws/2";
        private const string USER_AGENT = "Brainarr/1.0 (https://github.com/your-repo/brainarr)";
        

        public MusicBrainzService(HttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiter = new NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiter(logger);
            
            // Configure HttpClient for MusicBrainz
            _httpClient.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
            NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
        }

        public async Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle)
            => await ValidateArtistAlbumAsync(artistName, albumTitle, System.Threading.CancellationToken.None);

        public async Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
                    return false;

            _logger.Debug($"Validating artist/album with MusicBrainz: {artistName} - {albumTitle}");
                var searchResult = await SearchArtistAlbumAsync(artistName, albumTitle, cancellationToken);
                var isValid = searchResult != null && searchResult.Found;

                _logger.Debug($"MusicBrainz validation result for {artistName} - {albumTitle}: {isValid}");
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to validate artist/album with MusicBrainz: {artistName} - {albumTitle}");
                return false; // Assume invalid on error to be safe
            }
        }

        public async Task<bool> ValidateArtistAsync(string artistName)
            => await ValidateArtistAsync(artistName, System.Threading.CancellationToken.None);

        public async Task<bool> ValidateArtistAsync(string artistName, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(artistName))
                    return false;

                // Cache lookup
                var artistKey = $"artist:{NormalizeKey(artistName)}";
                if (TryGet(_artistExistsCache, artistKey, out var cachedExists))
                {
                    return cachedExists;
                }

                var encodedArtist = Uri.EscapeDataString(artistName);
                var searchUrl = $"{MUSICBRAINZ_BASE_URL}/artist/?query=artist:{encodedArtist}&fmt=json&limit=1";
                var response = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetAsync(searchUrl, ct), cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz artist search failed with status: {response.StatusCode}");
                    Set(_artistExistsCache, artistKey, false, NegativeTtl);
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var hasResults = !string.IsNullOrEmpty(content) && 
                               content.Contains("\"count\":") && 
                               !content.Contains("\"count\":0");

                _logger.Debug($"MusicBrainz artist validation for '{artistName}': {hasResults}");
                Set(_artistExistsCache, artistKey, hasResults, hasResults ? PositiveTtl : NegativeTtl);
                return hasResults;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to validate artist with MusicBrainz: {artistName}");
                return false;
            }
        }

        public async Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle)
            => await SearchArtistAlbumAsync(artistName, albumTitle, System.Threading.CancellationToken.None);

        public async Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var encodedArtist = Uri.EscapeDataString(artistName);
                var encodedAlbum = Uri.EscapeDataString(albumTitle);
                
                // Cache lookup for pair
                var key = $"pair:{NormalizeKey(artistName)}|{NormalizeKey(albumTitle)}";
                if (TryGet(_artistAlbumCache, key, out var cached))
                {
                    return cached ?? new MusicBrainzSearchResult { Found = false };
                }
                
                // Search for release groups (albums) by artist and title
                var searchUrl = $"{MUSICBRAINZ_BASE_URL}/release-group/?query=artist:{encodedArtist} AND releasegroup:{encodedAlbum}&fmt=json&limit=5";
                var response = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetAsync(searchUrl, ct), cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz search failed with status: {response.StatusCode}");
                    Set(_artistAlbumCache, key, null, NegativeTtl);
                    return new MusicBrainzSearchResult { Found = false };
                }
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseSearchResponse(content, artistName, albumTitle);

                _logger.Debug($"MusicBrainz search for '{artistName} - {albumTitle}': Found={result.Found}, Matches={result.MatchCount}");
                Set(_artistAlbumCache, key, result, result.Found ? PositiveTtl : NegativeTtl);
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search MusicBrainz for: {artistName} - {albumTitle}");
                return new MusicBrainzSearchResult { Found = false };
            }
        }

        // Throttling enforced via RateLimiter("musicbrainz")

        private MusicBrainzSearchResult ParseSearchResponse(string jsonContent, string searchArtist, string searchAlbum)
        {
            var result = new MusicBrainzSearchResult();

            try
            {
                // Simple JSON parsing for basic validation
                // In a production environment, you'd want to use a proper JSON parser
                if (string.IsNullOrEmpty(jsonContent))
                {
                    result.Found = false;
                    return result;
                }

                // Check if we have any results
                if (jsonContent.Contains("\"count\":0"))
                {
                    result.Found = false;
                    result.MatchCount = 0;
                    return result;
                }

                // Extract basic information
                var hasResults = jsonContent.Contains("\"release-groups\":[") && 
                               !jsonContent.Contains("\"release-groups\":[]");

                if (hasResults)
                {
                    // Count the number of matches (rough estimation)
                    var releaseGroupMatches = jsonContent.Split(new[] { "\"id\":\"" }, StringSplitOptions.None).Length - 1;
                    result.MatchCount = Math.Min(releaseGroupMatches, 5); // Limited by our search limit
                    
                    // For more sophisticated matching, we could parse the JSON properly
                    // and compare artist names, album titles, etc. for similarity
                    result.Found = result.MatchCount > 0;
                    
                    // Store raw response for potential future analysis
                    result.RawResponse = jsonContent.Length > 1000 ? 
                        jsonContent.Substring(0, 1000) + "..." : jsonContent;
                }
                else
                {
                    result.Found = false;
                    result.MatchCount = 0;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Error parsing MusicBrainz response");
                result.Found = false;
                return result;
            }
        }
    }

    /// <summary>
    /// Result of a MusicBrainz search operation.
    /// </summary>
    public class MusicBrainzSearchResult
    {
        /// <summary>
        /// Whether the artist/album combination was found in MusicBrainz.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// Number of potential matches found.
        /// </summary>
        public int MatchCount { get; set; }

        /// <summary>
        /// Confidence score of the best match (0.0-1.0).
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Details about the best match found.
        /// </summary>
        public MusicBrainzMatch BestMatch { get; set; }

        /// <summary>
        /// Raw response from MusicBrainz (for debugging).
        /// </summary>
        public string RawResponse { get; set; }
    }

    /// <summary>
    /// Information about a specific match from MusicBrainz.
    /// </summary>
    public class MusicBrainzMatch
    {
        /// <summary>
        /// MusicBrainz ID of the artist.
        /// </summary>
        public string ArtistId { get; set; } = string.Empty;

        /// <summary>
        /// MusicBrainz ID of the release group (album).
        /// </summary>
        public string ReleaseGroupId { get; set; } = string.Empty;

        /// <summary>
        /// Artist name as stored in MusicBrainz.
        /// </summary>
        public string ArtistName { get; set; } = string.Empty;

        /// <summary>
        /// Album title as stored in MusicBrainz.
        /// </summary>
        public string AlbumTitle { get; set; } = string.Empty;

        /// <summary>
        /// Release date from MusicBrainz.
        /// </summary>
        public DateTime? ReleaseDate { get; set; }

        /// <summary>
        /// Primary genre/type from MusicBrainz.
        /// </summary>
        public string PrimaryType { get; set; } = string.Empty;
    }

    // Internal cache helpers (scoped to this file)
    internal sealed class CacheEntry<T>
    {
        public T Value { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    // Move helpers inside main class to avoid partial conflicts
}

