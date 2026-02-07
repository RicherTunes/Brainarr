using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Filters recommendations with enhanced duplicate detection against the user's library.
    /// </summary>
    public class DuplicateFilterService : IDuplicateFilterService
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        public DuplicateFilterService(IArtistService artistService, IAlbumService albumService, Logger logger)
        {
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Filters recommendations with enhanced duplicate detection.
        /// </summary>
        public List<ImportListItemInfo> FilterDuplicates(List<ImportListItemInfo> recommendations)
        {
            var existingAlbums = _albumService.GetAllAlbums();
            var existingArtists = _artistService.GetAllArtists();

            // Build fast-lookup sets for robust duplicate detection
            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in existingArtists)
            {
                if (!string.IsNullOrWhiteSpace(artist.Name))
                {
                    // Normalize artist key variants
                    artistKeys.Add(artist.Name);
                    artistKeys.Add(artist.Name.Replace(" ", ""));
                    if (artist.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        artistKeys.Add(artist.Name.Substring(4));
                    }
                }
            }

            // Build a quick lookup for artistId -> artistName
            var artistNameById = existingArtists
                .Where(a => a != null)
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            foreach (var album in existingAlbums)
            {
                if (!artistNameById.TryGetValue(album.ArtistId, out var artistName) || string.IsNullOrWhiteSpace(artistName))
                    continue;

                // Multiple key formats for matching existing albums
                albumKeys.Add($"{artistName}_{album.Title}");
                albumKeys.Add($"{artistName.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");

                // Handle "The" prefix variations
                if (artistName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                {
                    var nameWithoutThe = artistName.Substring(4);
                    albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                }
            }

            var uniqueItems = new List<ImportListItemInfo>();
            var duplicatesFound = 0;

            foreach (var item in recommendations)
            {
                bool isDuplicate = false;

                var decodedArtist = DecodeComparisonValue(item.Artist);
                var decodedAlbum = DecodeComparisonValue(item.Album);

                if (!string.IsNullOrWhiteSpace(decodedArtist))
                {
                    item.Artist = decodedArtist;
                }

                if (!string.IsNullOrWhiteSpace(decodedAlbum))
                {
                    item.Album = decodedAlbum;
                }

                if (string.IsNullOrWhiteSpace(decodedAlbum))
                {
                    if (!string.IsNullOrWhiteSpace(decodedArtist))
                    {
                        var artistCandidates = new List<string?>
                        {
                            decodedArtist,
                            decodedArtist.Replace(" ", string.Empty),
                            decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                        }.Where(k => !string.IsNullOrWhiteSpace(k));

                        foreach (var candidate in artistCandidates)
                        {
                            if (artistKeys.Contains(candidate))
                            {
                                isDuplicate = true;
                                duplicatesFound++;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var normalizedArtist = decodedArtist;
                    var normalizedAlbum = decodedAlbum;

                    var keys = new[]
                    {
                        $"{normalizedArtist}_{normalizedAlbum}",
                        $"{normalizedArtist.Replace(" ", string.Empty)}_{normalizedAlbum.Replace(" ", string.Empty)}",
                        normalizedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                            ? $"{normalizedArtist.Substring(4)}_{normalizedAlbum}"
                            : null
                    }.Where(k => !string.IsNullOrWhiteSpace(k));

                    foreach (var key in keys)
                    {
                        if (albumKeys.Contains(key))
                        {
                            isDuplicate = true;
                            duplicatesFound++;
                            break;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    uniqueItems.Add(item);
                }
            }

            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered out {duplicatesFound} duplicate recommendation(s) using enhanced matching");
            }

            return uniqueItems;
        }


        public List<Recommendation> FilterExistingRecommendations(List<Recommendation> recommendations, bool artistMode)
        {
            if (recommendations == null || recommendations.Count == 0)
            {
                return recommendations ?? new List<Recommendation>();
            }

            var existingAlbums = _albumService.GetAllAlbums();
            var existingArtists = _artistService.GetAllArtists();

            var albumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var artistKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artist in existingArtists)
            {
                if (!string.IsNullOrWhiteSpace(artist.Name))
                {
                    artistKeys.Add(artist.Name);
                    artistKeys.Add(artist.Name.Replace(" ", ""));
                    if (artist.Name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                    {
                        artistKeys.Add(artist.Name.Substring(4));
                    }
                }
            }

            var artistNameById = existingArtists
                .Where(a => a != null)
                .GroupBy(a => a.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            foreach (var album in existingAlbums)
            {
                if (!artistNameById.TryGetValue(album.ArtistId, out var artistName) || string.IsNullOrWhiteSpace(artistName))
                    continue;

                albumKeys.Add($"{artistName}_{album.Title}");
                albumKeys.Add($"{artistName.Replace(" ", "")}_{album.Title?.Replace(" ", "")}");

                if (artistName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
                {
                    var nameWithoutThe = artistName.Substring(4);
                    albumKeys.Add($"{nameWithoutThe}_{album.Title}");
                }
            }

            var filtered = new List<Recommendation>();
            var duplicatesFound = 0;

            foreach (var rec in recommendations)
            {
                if (rec == null)
                {
                    continue;
                }

                bool isDuplicate = false;

                var decodedArtist = DecodeComparisonValue(rec.Artist);
                var decodedAlbum = DecodeComparisonValue(rec.Album);



                if (artistMode)
                {
                    var candidates = new List<string?>
                    {
                        decodedArtist,
                        decodedArtist.Replace(" ", string.Empty),
                        decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                    }.Where(k => !string.IsNullOrWhiteSpace(k));

                    if (candidates.Any(artistKeys.Contains))
                    {
                        duplicatesFound++;
                        isDuplicate = true;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(decodedAlbum))
                    {
                        if (!string.IsNullOrWhiteSpace(decodedArtist))
                        {
                            var artistCandidates = new List<string?>
                            {
                                decodedArtist,
                                decodedArtist.Replace(" ", string.Empty),
                                decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? decodedArtist.Substring(4) : null
                            }.Where(k => !string.IsNullOrWhiteSpace(k));

                            if (artistCandidates.Any(artistKeys.Contains))
                            {
                                duplicatesFound++;
                                isDuplicate = true;
                            }
                        }
                    }
                    else
                    {
                        var keys = new List<string?>
                        {
                            $"{decodedArtist}_{decodedAlbum}",
                            $"{decodedArtist.Replace(" ", string.Empty)}_{decodedAlbum.Replace(" ", string.Empty)}",
                            decodedArtist.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? $"{decodedArtist.Substring(4)}_{decodedAlbum}" : null
                        }.Where(k => !string.IsNullOrWhiteSpace(k));

                        if (keys.Any(albumKeys.Contains))
                        {
                            duplicatesFound++;
                            isDuplicate = true;
                        }
                    }
                }

                if (!isDuplicate)
                {
                    filtered.Add(rec);
                }
            }

            if (duplicatesFound > 0)
            {
                _logger.Info($"Filtered out {duplicatesFound} recommendation(s) already present in the library prior to validation");
            }

            return filtered;
        }

        private static string DecodeComparisonValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return WebUtility.HtmlDecode(value).Trim();
        }
    }
}
