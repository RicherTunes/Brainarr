using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;

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

        public MinimalResponseParser(Logger logger, HttpClient httpClient = null, RecommendationHistory history = null)
        {
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
            _history = history ?? new RecommendationHistory(logger);
        }

        /// <summary>
        /// Parse ultra-minimal response (just artist names) and enrich with details
        /// </summary>
        public async Task<List<Recommendation>> ParseAndEnrichAsync(string aiResponse)
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
                var enriched = await EnrichWithMusicBrainzAsync(artist);
                if (enriched != null)
                {
                    recommendations.Add(enriched);
                }

                // Rate limiting for MusicBrainz API
                await Task.Delay(100);
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
        private async Task<Recommendation> EnrichWithMusicBrainzAsync(string artistName)
        {
            try
            {
                // Query MusicBrainz for artist details
                var searchUrl = $"https://musicbrainz.org/ws/2/artist/?query={Uri.EscapeDataString(artistName)}&fmt=json&limit=1";

                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Brainarr/1.0 (https://github.com/brainarr)");

                var response = await _httpClient.GetStringAsync(searchUrl);
                var mbData = JsonSerializer.Deserialize<MusicBrainzResponse>(response);

                if (mbData?.Artists?.Any() == true)
                {
                    var artist = mbData.Artists.First();

                    // Get a popular album for this artist (optional)
                    string popularAlbum = null;
                    try
                    {
                        var releaseUrl = $"https://musicbrainz.org/ws/2/release-group/?artist={artist.Id}&type=album&fmt=json&limit=1";
                        var releaseResponse = await _httpClient.GetStringAsync(releaseUrl);
                        var releaseData = JsonSerializer.Deserialize<MusicBrainzReleaseResponse>(releaseResponse);
                        popularAlbum = releaseData?.ReleaseGroups?.FirstOrDefault()?.Title;
                    }
                    catch { }

                    return new Recommendation
                    {
                        Artist = artist.Name ?? artistName,
                        Album = popularAlbum ?? "Top Albums",
                        Genre = artist.Tags?.FirstOrDefault()?.Name ?? "Unknown",
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
                _logger.Warn($"Failed to enrich artist '{artistName}': {ex.Message}");

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
                                Artist = c.a ?? c.artist,
                                Album = c.l ?? c.album ?? "Albums",
                                Genre = c.g ?? c.genre ?? "Unknown",
                                Confidence = c.c > 0 ? c.c : 0.7,
                                Reason = c.r ?? "AI recommendation"
                            }).ToList();
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