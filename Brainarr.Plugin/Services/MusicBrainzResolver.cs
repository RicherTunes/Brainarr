using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public interface IMusicBrainzResolver
    {
        Task<ResolvedRecommendation> ResolveRecommendation(Recommendation rec);
        Artist FindBestArtistMatch(string artistName);
        Album FindBestAlbumMatch(string artistName, string albumName);
    }

    public class MusicBrainzResolver : IMusicBrainzResolver
    {
        private readonly ISearchForNewArtist _artistSearch;
        private readonly ISearchForNewAlbum _albumSearch;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private readonly Dictionary<string, Artist> _artistCache;
        private readonly Dictionary<string, Album> _albumCache;

        public MusicBrainzResolver(
            ISearchForNewArtist artistSearch,
            ISearchForNewAlbum albumSearch,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            _artistSearch = artistSearch;
            _albumSearch = albumSearch;
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
            _artistCache = new Dictionary<string, Artist>(StringComparer.OrdinalIgnoreCase);
            _albumCache = new Dictionary<string, Album>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<ResolvedRecommendation> ResolveRecommendation(Recommendation rec)
        {
            try
            {
                // First check if artist already exists in library
                var existingArtist = await Task.Run(() => FindExistingArtist(rec.Artist));
                if (existingArtist != null)
                {
                    _logger.Debug($"Artist '{rec.Artist}' already exists in library as '{existingArtist.Name}'");
                    return new ResolvedRecommendation
                    {
                        Status = ResolutionStatus.AlreadyInLibrary,
                        OriginalRecommendation = rec,
                        Reason = "Artist already in library"
                    };
                }

                // Search MusicBrainz for the artist
                var artist = await Task.Run(() => FindBestArtistMatch(rec.Artist));
                if (artist == null)
                {
                    _logger.Warn($"Could not find artist '{rec.Artist}' in MusicBrainz");
                    return new ResolvedRecommendation
                    {
                        Status = ResolutionStatus.NotFound,
                        OriginalRecommendation = rec,
                        Reason = "Artist not found in MusicBrainz"
                    };
                }

                // Check if this would map to "Various Artists"
                if (IsVariousArtists(artist))
                {
                    _logger.Info($"Rejecting '{rec.Artist}' as it maps to Various Artists");
                    return new ResolvedRecommendation
                    {
                        Status = ResolutionStatus.Invalid,
                        OriginalRecommendation = rec,
                        Reason = "Would map to Various Artists"
                    };
                }

                // Try to find the specific album
                Album album = null;
                if (!string.IsNullOrWhiteSpace(rec.Album))
                {
                    album = await Task.Run(() => FindBestAlbumMatch(artist.Name, rec.Album));
                }

                // Calculate confidence based on match quality
                var confidence = CalculateConfidence(rec, artist, album);

                return new ResolvedRecommendation
                {
                    Status = ResolutionStatus.Resolved,
                    OriginalRecommendation = rec,
                    Artist = artist,
                    Album = album,
                    ArtistMbId = artist.ForeignArtistId,
                    AlbumMbId = album?.ForeignAlbumId,
                    Confidence = confidence,
                    DisplayArtist = artist.Name,
                    DisplayAlbum = album?.Title ?? rec.Album
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to resolve recommendation: {rec.Artist} - {rec.Album}");
                return new ResolvedRecommendation
                {
                    Status = ResolutionStatus.Error,
                    OriginalRecommendation = rec,
                    Reason = ex.Message
                };
            }
        }

        public Artist FindBestArtistMatch(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
                return null;

            // Check cache first
            var cacheKey = artistName.ToLowerInvariant();
            if (_artistCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
                // Search MusicBrainz
                var searchResults = _artistSearch.SearchForNewArtist(artistName);
                if (searchResults == null || !searchResults.Any())
                {
                    _logger.Debug($"No MusicBrainz results for artist '{artistName}'");
                    return null;
                }

                // Score and rank results
                var bestMatch = searchResults
                    .Select(a => new
                    {
                        Artist = a,
                        Score = CalculateArtistMatchScore(artistName, a)
                    })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault(x => x.Score > 0.7); // Minimum confidence threshold

                if (bestMatch != null)
                {
                    _artistCache[cacheKey] = bestMatch.Artist;
                    _logger.Debug($"Found MusicBrainz match for '{artistName}': '{bestMatch.Artist.Name}' (score: {bestMatch.Score:F2})");
                    return bestMatch.Artist;
                }

                _logger.Debug($"No confident match for artist '{artistName}' in MusicBrainz");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"MusicBrainz search failed for artist '{artistName}'");
                return null;
            }
        }

        public Album FindBestAlbumMatch(string artistName, string albumName)
        {
            if (string.IsNullOrWhiteSpace(albumName))
                return null;

            var cacheKey = $"{artistName}_{albumName}".ToLowerInvariant();
            if (_albumCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            try
            {
                var searchResults = _albumSearch.SearchForNewAlbum(albumName, artistName);
                if (searchResults == null || !searchResults.Any())
                {
                    return null;
                }

                var bestMatch = searchResults
                    .Select(a => new
                    {
                        Album = a,
                        Score = CalculateAlbumMatchScore(albumName, artistName, a)
                    })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault(x => x.Score > 0.6);

                if (bestMatch != null)
                {
                    _albumCache[cacheKey] = bestMatch.Album;
                    return bestMatch.Album;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"MusicBrainz album search failed for '{artistName} - {albumName}'");
                return null;
            }
        }

        private Artist FindExistingArtist(string artistName)
        {
            var normalized = NormalizeForComparison(artistName);
            
            return _artistService.GetAllArtists()
                .FirstOrDefault(a => 
                    NormalizeForComparison(a.Name) == normalized ||
                    (a.Metadata?.Value?.Name != null && NormalizeForComparison(a.Metadata.Value.Name) == normalized));
        }

        private bool IsVariousArtists(Artist artist)
        {
            if (artist == null) return false;
            
            var name = artist.Name?.ToLowerInvariant() ?? "";
            var mbId = artist.ForeignArtistId?.ToLowerInvariant() ?? "";
            
            // Check for known Various Artists MBIDs
            var variousArtistsMbIds = new[]
            {
                "89ad4ac3-39f7-470e-963a-56509c546377", // Various Artists
                "f731ccc4-e22a-43af-a747-64213329e088"  // [anonymous]
            };
            
            return variousArtistsMbIds.Any(id => mbId.Contains(id)) ||
                   name.Contains("various") ||
                   name.Contains("compilation") ||
                   name == "va";
        }

        private double CalculateArtistMatchScore(string searchTerm, Artist artist)
        {
            var name1 = NormalizeForComparison(searchTerm);
            var name2 = NormalizeForComparison(artist.Name);
            
            // Exact match
            if (name1 == name2) return 1.0;
            
            // Calculate similarity
            var similarity = CalculateStringSimilarity(name1, name2);
            
            // Boost score if artist has albums (more likely to be real)
            if (artist.Albums?.Value?.Any() == true)
            {
                similarity += 0.1;
            }
            
            // Penalize if it looks like Various Artists
            if (IsVariousArtists(artist))
            {
                similarity *= 0.1;
            }
            
            return Math.Min(1.0, similarity);
        }

        private double CalculateAlbumMatchScore(string searchAlbum, string searchArtist, Album album)
        {
            var albumSimilarity = CalculateStringSimilarity(
                NormalizeForComparison(searchAlbum),
                NormalizeForComparison(album.Title));
            
            var artistSimilarity = 1.0;
            if (album.ArtistMetadata?.Value?.Name != null)
            {
                artistSimilarity = CalculateStringSimilarity(
                    NormalizeForComparison(searchArtist),
                    NormalizeForComparison(album.ArtistMetadata.Value.Name));
            }
            
            // Weighted average: album title is more important
            return (albumSimilarity * 0.7) + (artistSimilarity * 0.3);
        }

        private double CalculateStringSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;
            
            if (s1 == s2) return 1.0;
            
            // Levenshtein distance normalized by max length
            var distance = LevenshteinDistance(s1, s2);
            var maxLength = Math.Max(s1.Length, s2.Length);
            
            return 1.0 - ((double)distance / maxLength);
        }

        private int LevenshteinDistance(string s1, string s2)
        {
            var dp = new int[s1.Length + 1, s2.Length + 1];
            
            for (int i = 0; i <= s1.Length; i++)
                dp[i, 0] = i;
            
            for (int j = 0; j <= s2.Length; j++)
                dp[0, j] = j;
            
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);
                }
            }
            
            return dp[s1.Length, s2.Length];
        }

        private string NormalizeForComparison(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            
            // Remove common prefixes and normalize
            text = text.Trim().ToLowerInvariant();
            
            if (text.StartsWith("the "))
                text = text.Substring(4);
            
            // Remove special characters
            text = System.Text.RegularExpressions.Regex.Replace(text, @"[^\w\s]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            
            return text.Trim();
        }

        private double CalculateConfidence(Recommendation rec, Artist artist, Album album)
        {
            var artistScore = CalculateArtistMatchScore(rec.Artist, artist);
            
            if (album != null)
            {
                var albumScore = CalculateAlbumMatchScore(rec.Album, rec.Artist, album);
                return (artistScore * 0.6) + (albumScore * 0.4);
            }
            
            return artistScore * 0.8; // Lower confidence without album match
        }
    }

    public class ResolvedRecommendation
    {
        public ResolutionStatus Status { get; set; }
        public Recommendation OriginalRecommendation { get; set; }
        public Artist Artist { get; set; }
        public Album Album { get; set; }
        public string ArtistMbId { get; set; }
        public string AlbumMbId { get; set; }
        public double Confidence { get; set; }
        public string DisplayArtist { get; set; }
        public string DisplayAlbum { get; set; }
        public string Reason { get; set; }
    }

    public enum ResolutionStatus
    {
        Resolved,
        AlreadyInLibrary,
        NotFound,
        Invalid,
        Error
    }
}