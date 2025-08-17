using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Parses ultra-minimal AI responses and enriches them with MusicBrainz data
    /// Optimized for minimal token usage in both directions
    /// </summary>
    public class MinimalResponseParser : IDisposable
    {
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private readonly RecommendationHistory _history;
        private readonly IRateLimiter _rateLimiter;
        private readonly bool _ownsHttpClient;
        
        // Security validation patterns
        private static readonly Regex ValidArtistNamePattern = new Regex(
            @"^[\p{L}\p{N}\s\-\.,'&!()]+$", 
            RegexOptions.Compiled);
        
        private static readonly Regex InvalidCharacterPattern = new Regex(
            @"[<>\""\\/;`%\0\r\n\t]|(\-\-)|(/\*)|(\*/)", 
            RegexOptions.Compiled);

        // Limits for security
        private const int MaxArtistNameLength = 100;
        private const int MaxArtistsToProcess = 50;
        private const int MusicBrainzTimeout = 5000; // 5 seconds

        public MinimalResponseParser(
            Logger logger, 
            HttpClient httpClient = null, 
            RecommendationHistory history = null,
            IRateLimiter rateLimiter = null)
        {
            _logger = logger;
            _history = history ?? new RecommendationHistory(logger);
            _rateLimiter = rateLimiter ?? new RateLimiter(logger);
            
            if (httpClient == null)
            {
                _ownsHttpClient = true;
                _httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(MusicBrainzTimeout)
                };
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"Brainarr/{Constants.PluginVersion} (https://github.com/brainarr)");
            }
            else
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
            }

            // Configure rate limiting for MusicBrainz
            _rateLimiter.Configure("musicbrainz", 1, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Validates and sanitizes artist name for security
        /// </summary>
        private string ValidateAndSanitizeArtistName(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return null;

            // Trim and check length
            artistName = artistName.Trim();
            if (artistName.Length > MaxArtistNameLength)
            {
                _logger.Warn($"Artist name too long ({artistName.Length} chars), truncating");
                artistName = artistName.Substring(0, MaxArtistNameLength);
            }

            // Check for invalid characters
            if (InvalidCharacterPattern.IsMatch(artistName))
            {
                _logger.Warn($"Artist name contains invalid characters: {artistName}");
                // Remove invalid characters instead of rejecting
                artistName = InvalidCharacterPattern.Replace(artistName, "");
            }

            // Additional validation
            if (!ValidArtistNamePattern.IsMatch(artistName))
            {
                _logger.Warn($"Artist name failed validation: {artistName}");
                return null;
            }

            // Check for SQL injection patterns
            if (RecommendationSanitizer.ContainsSqlInjection(artistName))
            {
                _logger.Warn($"Potential SQL injection in artist name: {artistName}");
                return null;
            }

            return artistName;
        }

        /// <summary>
        /// Parse ultra-minimal response (just artist names) and enrich with details
        /// </summary>
        public async Task<List<Recommendation>> ParseAndEnrichAsync(string aiResponse)
        {
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                _logger.Warn("Empty AI response received");
                return new List<Recommendation>();
            }

            // Sanitize the entire response first
            aiResponse = RecommendationSanitizer.SanitizeInput(aiResponse);

            var artistNames = ExtractArtistNames(aiResponse);
            
            if (!artistNames.Any())
            {
                _logger.Warn("No valid artist names found in AI response");
                return new List<Recommendation>();
            }

            // Limit the number of artists to process
            if (artistNames.Count > MaxArtistsToProcess)
            {
                _logger.Warn($"Too many artists ({artistNames.Count}), limiting to {MaxArtistsToProcess}");
                artistNames = artistNames.Take(MaxArtistsToProcess).ToList();
            }

            _logger.Info($"Parsed {artistNames.Count} artist names from minimal response");

            // Filter out excluded artists
            var exclusions = _history.GetExclusions();
            var newArtists = artistNames.Where(artist => 
            {
                var key = artist.ToLowerInvariant();
                return !exclusions.InLibrary.Contains(key) && 
                       !exclusions.RecentlyRejected.Contains(key) &&
                       !exclusions.OverSuggested.Contains(key);
            }).ToList();

            _logger.Info($"After exclusions: {newArtists.Count} new artists to process");

            // Enrich with MusicBrainz data
            var recommendations = new List<Recommendation>();
            foreach (var artist in newArtists)
            {
                try
                {
                    var enriched = await EnrichWithMusicBrainzAsync(artist);
                    if (enriched != null)
                    {
                        recommendations.Add(enriched);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to enrich artist '{artist}': {ex.Message}");
                }
            }

            return recommendations;
        }

        /// <summary>
        /// Extract artist names from various response formats
        /// </summary>
        private List<string> ExtractArtistNames(string response)
        {
            var artists = new List<string>();

            try
            {
                // Try to parse as JSON array first
                if (response.Contains("[") && response.Contains("]"))
                {
                    var jsonStart = response.IndexOf('[');
                    var jsonEnd = response.LastIndexOf(']');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        
                        try
                        {
                            // Try simple string array first (most compact)
                            var simpleArray = JsonSerializer.Deserialize<List<string>>(json);
                            if (simpleArray != null)
                            {
                                foreach (var artist in simpleArray)
                                {
                                    var validated = ValidateAndSanitizeArtistName(artist);
                                    if (!string.IsNullOrWhiteSpace(validated))
                                    {
                                        artists.Add(validated);
                                    }
                                }
                                return artists;
                            }
                        }
                        catch
                        {
                            // Try object array with "a" or "artist" property
                            try
                            {
                                var complexArray = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                                if (complexArray != null)
                                {
                                    foreach (var item in complexArray)
                                    {
                                        string artistName = null;
                                        if (item.ContainsKey("a"))
                                            artistName = item["a"]?.ToString();
                                        else if (item.ContainsKey("artist"))
                                            artistName = item["artist"]?.ToString();
                                        else if (item.ContainsKey("name"))
                                            artistName = item["name"]?.ToString();

                                        if (!string.IsNullOrWhiteSpace(artistName))
                                        {
                                            var validated = ValidateAndSanitizeArtistName(artistName);
                                            if (!string.IsNullOrWhiteSpace(validated))
                                            {
                                                artists.Add(validated);
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // Fallback: Try to extract from plain text (line by line)
                if (!artists.Any())
                {
                    var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        // Look for lines that look like artist names
                        if (!string.IsNullOrWhiteSpace(trimmed) && 
                            !trimmed.StartsWith("{") && 
                            !trimmed.StartsWith("[") &&
                            !trimmed.Contains("recommendation", StringComparison.OrdinalIgnoreCase) &&
                            !trimmed.Contains("suggest", StringComparison.OrdinalIgnoreCase) &&
                            trimmed.Length > 2 && 
                            trimmed.Length < MaxArtistNameLength)
                        {
                            // Remove common prefixes like "1.", "-", "*"
                            var cleaned = Regex.Replace(trimmed, @"^[\d\.\-\*\s]+", "");
                            var validated = ValidateAndSanitizeArtistName(cleaned);
                            if (!string.IsNullOrWhiteSpace(validated))
                            {
                                artists.Add(validated);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SecureLogger.LogError(_logger, "Error extracting artist names", ex);
            }

            return artists.Distinct().Take(MaxArtistsToProcess).ToList();
        }

        /// <summary>
        /// Enrich artist name with MusicBrainz data
        /// </summary>
        private async Task<Recommendation> EnrichWithMusicBrainzAsync(string artistName)
        {
            // Validate artist name one more time
            artistName = ValidateAndSanitizeArtistName(artistName);
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return null;
            }

            return await _rateLimiter.ExecuteAsync("musicbrainz", async () =>
            {
                try
                {
                    // Properly encode the artist name for URL
                    var encodedArtist = Uri.EscapeDataString(artistName);
                    
                    // Validate encoded string doesn't exceed reasonable URL length
                    if (encodedArtist.Length > 200)
                    {
                        _logger.Warn($"Encoded artist name too long: {encodedArtist.Length} chars");
                        return CreateFallbackRecommendation(artistName);
                    }

                    // Query MusicBrainz for artist details with HTTPS enforced
                    var searchUrl = $"https://musicbrainz.org/ws/2/artist/?query={encodedArtist}&fmt=json&limit=1";
                    
                    using var response = await _httpClient.GetAsync(searchUrl);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Warn($"MusicBrainz API returned {response.StatusCode} for artist '{artistName}'");
                        return CreateFallbackRecommendation(artistName);
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    var mbData = JsonSerializer.Deserialize<MusicBrainzResponse>(content);
                    
                    if (mbData?.Artists?.Any() == true)
                    {
                        var artist = mbData.Artists.First();
                        
                        // Validate the returned data
                        var validatedName = ValidateAndSanitizeArtistName(artist.Name) ?? artistName;
                        
                        // Get a popular album for this artist (optional)
                        string popularAlbum = null;
                        if (!string.IsNullOrWhiteSpace(artist.Id) && IsValidGuid(artist.Id))
                        {
                            try
                            {
                                var releaseUrl = $"https://musicbrainz.org/ws/2/release-group/?artist={artist.Id}&type=album&fmt=json&limit=1";
                                using var releaseResponse = await _httpClient.GetAsync(releaseUrl);
                                
                                if (releaseResponse.IsSuccessStatusCode)
                                {
                                    var releaseContent = await releaseResponse.Content.ReadAsStringAsync();
                                    var releaseData = JsonSerializer.Deserialize<MusicBrainzReleaseResponse>(releaseContent);
                                    popularAlbum = ValidateAndSanitizeAlbumName(releaseData?.ReleaseGroups?.FirstOrDefault()?.Title);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug($"Failed to get album for artist: {ex.Message}");
                            }
                        }
                        
                        return new Recommendation
                        {
                            Artist = validatedName,
                            Album = popularAlbum ?? "Top Albums",
                            Genre = ValidateGenre(artist.Tags?.FirstOrDefault()?.Name) ?? "Unknown",
                            Confidence = 0.8,
                            Reason = "Similar artists based on your library"
                        };
                    }
                    
                    return CreateFallbackRecommendation(artistName);
                }
                catch (HttpRequestException ex)
                {
                    _logger.Warn($"HTTP error enriching artist '{artistName}': {ex.Message}");
                    return CreateFallbackRecommendation(artistName);
                }
                catch (TaskCanceledException)
                {
                    _logger.Warn($"Timeout enriching artist '{artistName}'");
                    return CreateFallbackRecommendation(artistName);
                }
                catch (Exception ex)
                {
                    SecureLogger.LogWarn(_logger, $"Failed to enrich artist '{artistName}': {ex.Message}");
                    return CreateFallbackRecommendation(artistName);
                }
            });
        }

        private Recommendation CreateFallbackRecommendation(string artistName)
        {
            return new Recommendation
            {
                Artist = artistName,
                Album = "Discography",
                Genre = "Unknown",
                Confidence = 0.6,
                Reason = "AI recommendation"
            };
        }

        private bool IsValidGuid(string guid)
        {
            return !string.IsNullOrWhiteSpace(guid) && 
                   Guid.TryParse(guid, out _);
        }

        private string ValidateAndSanitizeAlbumName(string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName))
                return null;

            albumName = albumName.Trim();
            if (albumName.Length > 150)
            {
                albumName = albumName.Substring(0, 150);
            }

            // Remove invalid characters
            albumName = InvalidCharacterPattern.Replace(albumName, "");
            
            return string.IsNullOrWhiteSpace(albumName) ? null : albumName;
        }

        private string ValidateGenre(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
                return null;

            genre = genre.Trim();
            if (genre.Length > 50)
            {
                genre = genre.Substring(0, 50);
            }

            // Remove invalid characters
            genre = InvalidCharacterPattern.Replace(genre, "");
            
            return string.IsNullOrWhiteSpace(genre) ? null : genre;
        }

        /// <summary>
        /// Parse standard format responses (for backwards compatibility)
        /// </summary>
        public List<Recommendation> ParseStandardResponse(string response)
        {
            var recommendations = new List<Recommendation>();

            if (string.IsNullOrWhiteSpace(response))
            {
                return recommendations;
            }

            // Sanitize input first
            response = RecommendationSanitizer.SanitizeInput(response);

            try
            {
                // Try to extract JSON from the response
                var jsonStart = response.IndexOf('[');
                var jsonEnd = response.LastIndexOf(']');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    
                    // Try full format
                    try
                    {
                        recommendations = JsonSerializer.Deserialize<List<Recommendation>>(json, 
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch
                    {
                        // Try compact format
                        var compact = JsonSerializer.Deserialize<List<CompactRecommendation>>(json);
                        if (compact != null)
                        {
                            recommendations = compact.Select(c => new Recommendation
                            {
                                Artist = ValidateAndSanitizeArtistName(c.a ?? c.artist) ?? "Unknown",
                                Album = ValidateAndSanitizeAlbumName(c.l ?? c.album) ?? "Albums",
                                Genre = ValidateGenre(c.g ?? c.genre) ?? "Unknown",
                                Confidence = c.c > 0 && c.c <= 1 ? c.c : 0.7,
                                Reason = RecommendationSanitizer.SanitizeInput(c.r) ?? "AI recommendation"
                            }).Where(r => !string.IsNullOrWhiteSpace(r.Artist)).ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SecureLogger.LogError(_logger, "Error parsing standard response", ex);
            }

            // Validate and sanitize all recommendations
            recommendations = recommendations
                .Where(r => RecommendationSanitizer.ValidateRecommendation(r))
                .Take(MaxArtistsToProcess)
                .ToList();

            // Filter out excluded items
            if (recommendations.Any() && _history != null)
            {
                var exclusions = _history.GetExclusions();
                recommendations = recommendations.Where(r =>
                {
                    var key = r.Artist.ToLowerInvariant();
                    var albumKey = string.IsNullOrEmpty(r.Album) ? key : $"{key}|{r.Album.ToLowerInvariant()}";
                    
                    return !exclusions.InLibrary.Contains(key) && 
                           !exclusions.InLibrary.Contains(albumKey) &&
                           !exclusions.RecentlyRejected.Contains(key) &&
                           !exclusions.OverSuggested.Contains(key);
                }).ToList();
            }

            return recommendations;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _httpClient?.Dispose();
            }
        }

        // Compact format for minimal responses
        private class CompactRecommendation
        {
            public string a { get; set; } // artist
            public string artist { get; set; }
            public string l { get; set; } // album
            public string album { get; set; }
            public string g { get; set; } // genre
            public string genre { get; set; }
            public double c { get; set; } // confidence
            public string r { get; set; } // reason
        }

        // MusicBrainz API response models
        private class MusicBrainzResponse
        {
            public List<MusicBrainzArtist> Artists { get; set; }
        }

        private class MusicBrainzArtist
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public List<MusicBrainzTag> Tags { get; set; }
        }

        private class MusicBrainzTag
        {
            public string Name { get; set; }
        }

        private class MusicBrainzReleaseResponse
        {
            public List<MusicBrainzReleaseGroup> ReleaseGroups { get; set; }
        }

        private class MusicBrainzReleaseGroup
        {
            public string Title { get; set; }
        }
    }
}