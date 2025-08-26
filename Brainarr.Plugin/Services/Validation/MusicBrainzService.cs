using System;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;

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
    }

    /// <summary>
    /// MusicBrainz service for validating music recommendations against the open music database.
    /// </summary>
    public class MusicBrainzService : IMusicBrainzService
    {
        private readonly HttpClient _httpClient;
        private readonly Logger _logger;
        
        private const string MUSICBRAINZ_BASE_URL = "https://musicbrainz.org/ws/2";
        private const string USER_AGENT = "Brainarr/1.0 (https://github.com/your-repo/brainarr)";
        private const int REQUEST_DELAY_MS = 1000; // MusicBrainz rate limiting
        
        private DateTime _lastRequest = DateTime.MinValue;

        public MusicBrainzService(HttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Configure HttpClient for MusicBrainz
            _httpClient.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
        }

        public async Task<bool> ValidateArtistAlbumAsync(string artistName, string albumTitle)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(albumTitle))
                    return false;

                _logger.Debug($"Validating artist/album with MusicBrainz: {artistName} - {albumTitle}");

                var searchResult = await SearchArtistAlbumAsync(artistName, albumTitle);
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
        {
            try
            {
                if (string.IsNullOrWhiteSpace(artistName))
                    return false;

                await EnsureRateLimit();

                var encodedArtist = Uri.EscapeDataString(artistName);
                var searchUrl = $"{MUSICBRAINZ_BASE_URL}/artist/?query=artist:{encodedArtist}&fmt=json&limit=1";

                var response = await _httpClient.GetAsync(searchUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz artist search failed with status: {response.StatusCode}");
                    return false;
                }

                var content = await response.Content.ReadAsStringAsync();
                var hasResults = !string.IsNullOrEmpty(content) && 
                               content.Contains("\"count\":") && 
                               !content.Contains("\"count\":0");

                _logger.Debug($"MusicBrainz artist validation for '{artistName}': {hasResults}");
                return hasResults;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to validate artist with MusicBrainz: {artistName}");
                return false;
            }
        }

        public async Task<MusicBrainzSearchResult> SearchArtistAlbumAsync(string artistName, string albumTitle)
        {
            try
            {
                await EnsureRateLimit();

                var encodedArtist = Uri.EscapeDataString(artistName);
                var encodedAlbum = Uri.EscapeDataString(albumTitle);
                
                // Search for release groups (albums) by artist and title
                var searchUrl = $"{MUSICBRAINZ_BASE_URL}/release-group/?query=artist:{encodedArtist} AND releasegroup:{encodedAlbum}&fmt=json&limit=5";

                var response = await _httpClient.GetAsync(searchUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Debug($"MusicBrainz search failed with status: {response.StatusCode}");
                    return new MusicBrainzSearchResult { Found = false };
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = ParseSearchResponse(content, artistName, albumTitle);

                _logger.Debug($"MusicBrainz search for '{artistName} - {albumTitle}': Found={result.Found}, Matches={result.MatchCount}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, $"Failed to search MusicBrainz for: {artistName} - {albumTitle}");
                return new MusicBrainzSearchResult { Found = false };
            }
        }

        private async Task EnsureRateLimit()
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequest;
            if (timeSinceLastRequest.TotalMilliseconds < REQUEST_DELAY_MS)
            {
                var delayMs = REQUEST_DELAY_MS - (int)timeSinceLastRequest.TotalMilliseconds;
                await Task.Delay(delayMs);
            }
            _lastRequest = DateTime.UtcNow;
        }

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
}