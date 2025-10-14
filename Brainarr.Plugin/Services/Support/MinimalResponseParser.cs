using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NLog;
using Brainarr.Plugin.Models;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Parses ultra-minimal AI responses and enriches them with MusicBrainz data
    /// Optimized for minimal token usage in both directions
    /// </summary>
    public class MinimalResponseParser
    {
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private readonly RecommendationHistory _history;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter _rateLimiter;

        public MinimalResponseParser(Logger logger, HttpClient? httpClient = null, RecommendationHistory? history = null, NzbDrone.Core.ImportLists.Brainarr.Services.IRateLimiter? rateLimiter = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? MusicBrainzRateLimiter.CreateMusicBrainzClient();
            _history = history ?? new RecommendationHistory(_logger);
            _rateLimiter = rateLimiter ?? new NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiter(_logger);
            NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
        }

        /// <summary>
        /// Parse ultra-minimal response (just artist names) and enrich with details
        /// </summary>
        public async Task<List<Recommendation>> ParseAndEnrichAsync(string aiResponse, System.Threading.CancellationToken cancellationToken = default)
        {
            var artistNames = ExtractArtistNames(aiResponse);

            if (!artistNames.Any())
            {
                _logger.Warn("No artist names found in AI response");
                return new List<Recommendation>();
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
                if (cancellationToken.IsCancellationRequested) break;
                var enriched = await EnrichWithMusicBrainzAsync(artist, cancellationToken);
                if (enriched != null)
                {
                    recommendations.Add(enriched);
                }

                // Throttling handled centrally via RateLimiter("musicbrainz")
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
                                artists.AddRange(simpleArray.Where(a => !string.IsNullOrWhiteSpace(a)));
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
                                        if (item.ContainsKey("a"))
                                            artists.Add(item["a"].ToString());
                                        else if (item.ContainsKey("artist"))
                                            artists.Add(item["artist"].ToString());
                                        else if (item.ContainsKey("name"))
                                            artists.Add(item["name"].ToString());
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
                            trimmed.Length < 100)
                        {
                            // Remove common prefixes like "1.", "-", "*"
                            var cleaned = System.Text.RegularExpressions.Regex.Replace(trimmed, @"^[\d\.\-\*\s]+", "");
                            if (!string.IsNullOrWhiteSpace(cleaned))
                            {
                                artists.Add(cleaned);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error extracting artist names");
            }

            return artists.Distinct().ToList();
        }

        /// <summary>
        /// Enrich artist name with MusicBrainz data
        /// </summary>
        private async Task<Recommendation> EnrichWithMusicBrainzAsync(string artistName, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                // Query MusicBrainz for artist details with rate limiting
                var searchUrl = $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString(artistName)}&fmt=json&limit=1";

                var response = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetStringAsync(searchUrl, ct), cancellationToken);

                var mbData = SecureJsonSerializer.Deserialize<ProviderResponses.MusicBrainzResponse>(response);

                if (mbData?.Artists?.Any() == true)
                {
                    var artist = mbData.Artists.First();

                    // Get a popular album for this artist (optional)
                    string popularAlbum = null;
                    try
                    {
                        var releaseUrl = $"https://musicbrainz.org/ws/2/release-group/?artist={artist.Id}&type=album&fmt=json&limit=1";
                        var releaseResponse = await _rateLimiter.ExecuteAsync("musicbrainz", async (ct) => await _httpClient.GetStringAsync(releaseUrl, ct), cancellationToken);
                        var releaseData = SecureJsonSerializer.Deserialize<ProviderResponses.MusicBrainzResponse>(releaseResponse);
                        popularAlbum = releaseData?.Releases?.FirstOrDefault()?.Title;
                    }
                    catch { }

                    return new Recommendation
                    {
                        Artist = artist.Name ?? artistName,
                        Album = popularAlbum ?? "Top Albums",
                        Genre = "Unknown", // MusicBrainz tags not in current response model
                        Confidence = 0.8, // Default confidence for enriched data
                        Reason = $"Similar artists based on your library"
                    };
                }

                // Fallback if MusicBrainz doesn't have the artist
                return new Recommendation
                {
                    Artist = artistName,
                    Album = "Discography",
                    Genre = "Unknown",
                    Confidence = 0.6,
                    Reason = "AI recommendation"
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to enrich artist '{artistName}'");
                _logger.Debug($"MusicBrainz error details: {ex.Message}");

                // Return basic recommendation without enrichment
                return new Recommendation
                {
                    Artist = artistName,
                    Album = "Albums",
                    Genre = "Unknown",
                    Confidence = 0.5,
                    Reason = "AI recommendation (no metadata available)"
                };
            }
        }

        /// <summary>
        /// Parse standard format responses (for backwards compatibility)
        /// </summary>
        private static readonly System.Text.Json.JsonSerializerOptions CaseInsensitiveOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public List<Recommendation> ParseStandardResponse(string response)
        {
            var recommendations = new List<Recommendation>();

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
                        recommendations = JsonSerializer.Deserialize<List<Recommendation>>(json, CaseInsensitiveOptions);

                        // Check if deserialization actually populated the fields (not just defaults)
                        // If ANY item has empty Artist field, it's likely a compact/alternative format
                        if (recommendations != null && recommendations.Any() &&
                            recommendations.Any(r => string.IsNullOrEmpty(r.Artist)))
                        {
                            // At least one field is empty, probably compact/alternative format - try that instead
                            recommendations = null;
                        }
                        else if (recommendations != null)
                        {
                            // Apply default values for missing fields
                            recommendations = recommendations.Select(r => r with
                            {
                                Album = string.IsNullOrEmpty(r.Album) ? "Albums" : r.Album,
                                Genre = string.IsNullOrEmpty(r.Genre) ? "Unknown" : r.Genre,
                                Confidence = r.Confidence == 0 ? 0.7 : r.Confidence
                            }).ToList();
                        }
                    }
                    catch
                    {
                        recommendations = null;
                    }

                    // If full format didn't work, try compact format
                    if (recommendations == null || !recommendations.Any())
                    {
                        try
                        {
                            var compact = JsonSerializer.Deserialize<List<CompactRecommendation>>(json);
                            if (compact != null)
                            {
                                recommendations = compact.Select(c => new Recommendation
                                {
                                    Artist = c.a ?? c.artist ?? c.name,
                                    Album = c.l ?? c.album ?? "Albums",
                                    Genre = c.g ?? c.genre ?? "Unknown",
                                    Confidence = c.c > 0 ? c.c : 0.7,
                                    Reason = c.r ?? "AI recommendation"
                                }).ToList();
                            }
                        }
                        catch
                        {
                            // If both formats fail, return empty list
                            recommendations = new List<Recommendation>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error parsing standard response");
            }

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

        // Compact format for minimal responses
        private class CompactRecommendation
        {
            public string a { get; set; } = string.Empty; // artist
            public string artist { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty; // alternative for artist
            public string l { get; set; } = string.Empty; // album
            public string album { get; set; } = string.Empty;
            public string g { get; set; } = string.Empty; // genre
            public string genre { get; set; } = string.Empty;
            public double c { get; set; } // confidence
            public string r { get; set; } = string.Empty; // reason
        }

        // MusicBrainz API response models
        private class MusicBrainzResponse
        {
            public List<MusicBrainzArtist> Artists { get; set; } = new List<MusicBrainzArtist>();
        }

        private class MusicBrainzArtist
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public List<MusicBrainzTag> Tags { get; set; } = new List<MusicBrainzTag>();
        }

        private class MusicBrainzTag
        {
            public string Name { get; set; } = string.Empty;
        }

        private class MusicBrainzReleaseResponse
        {
            public List<MusicBrainzReleaseGroup> ReleaseGroups { get; set; } = new List<MusicBrainzReleaseGroup>();
        }

        private class MusicBrainzReleaseGroup
        {
            public string Title { get; set; } = string.Empty;
        }
    }
}
