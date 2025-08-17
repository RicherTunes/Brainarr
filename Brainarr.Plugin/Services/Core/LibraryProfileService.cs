using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NLog;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class LibraryProfileService : ILibraryProfileService
    {
        private readonly ILibraryAnalyzer _libraryAnalyzer;
        private readonly Logger _logger;

        public LibraryProfileService(
            ILibraryAnalyzer libraryAnalyzer,
            Logger logger)
        {
            _libraryAnalyzer = libraryAnalyzer;
            _logger = logger;
        }

        public LibraryProfile GenerateLibraryProfile(
            IEnumerable<Artist> artists,
            IEnumerable<Album> albums,
            BrainarrSettings settings)
        {
            var artistList = artists?.ToList() ?? new List<Artist>();
            var albumList = albums?.ToList() ?? new List<Album>();

            _logger.Debug($"Generating library profile from {artistList.Count} artists and {albumList.Count} albums");

            var profile = new LibraryProfile
            {
                TotalArtists = artistList.Count,
                TotalAlbums = albumList.Count,
                Genres = ExtractGenres(artistList, albumList),
                TopArtists = GetTopArtists(artistList, settings.MaxArtistsToAnalyze),
                RecentAlbums = GetRecentAlbums(albumList, settings.MaxAlbumsToAnalyze),
                DecadeDistribution = CalculateDecadeDistribution(albumList),
                PreferredStyles = DeterminePreferredStyles(artistList, albumList)
            };

            if (settings.EnableDeepAnalysis)
            {
                EnrichProfileWithDeepAnalysis(profile, artistList, albumList);
            }

            return profile;
        }

        public string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            if (profile == null) return "empty";

            var fingerprintData = new StringBuilder();
            fingerprintData.Append(profile.TotalArtists);
            fingerprintData.Append(profile.TotalAlbums);
            fingerprintData.Append(string.Join(",", profile.Genres.OrderBy(g => g)));
            fingerprintData.Append(string.Join(",", profile.TopArtists.OrderBy(a => a)));

            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprintData.ToString()));
                return Convert.ToBase64String(hashBytes);
            }
        }

        public LibraryProfile GetEnhancedLibraryProfile(
            IArtistService artistService,
            IAlbumService albumService,
            BrainarrSettings settings)
        {
            var artists = artistService.GetAllArtists();
            var albums = albumService.GetAllAlbums();

            var profile = GenerateLibraryProfile(artists, albums, settings);
            
            profile.AnalysisMetadata = new AnalysisMetadata
            {
                AnalyzedAt = DateTime.UtcNow,
                Version = "2.0",
                DeepAnalysisEnabled = settings.EnableDeepAnalysis
            };

            _logger.Info($"Generated enhanced library profile: {profile.TotalArtists} artists, " +
                        $"{profile.TotalAlbums} albums, {profile.Genres.Count} genres");

            return profile;
        }

        private List<string> ExtractGenres(List<Artist> artists, List<Album> albums)
        {
            var genres = new HashSet<string>();

            foreach (var artist in artists.Where(a => a.Genres != null))
            {
                foreach (var genre in artist.Genres)
                {
                    genres.Add(genre);
                }
            }

            foreach (var album in albums.Where(a => a.Genres != null))
            {
                foreach (var genre in album.Genres)
                {
                    genres.Add(genre);
                }
            }

            return genres.OrderBy(g => g).ToList();
        }

        private List<string> GetTopArtists(List<Artist> artists, int maxCount)
        {
            return artists
                .OrderByDescending(a => a.Statistics?.AlbumCount ?? 0)
                .ThenByDescending(a => a.Ratings?.Value ?? 0)
                .Take(maxCount)
                .Select(a => a.Name)
                .ToList();
        }

        private List<string> GetRecentAlbums(List<Album> albums, int maxCount)
        {
            return albums
                .Where(a => a.ReleaseDate.HasValue)
                .OrderByDescending(a => a.ReleaseDate.Value)
                .Take(maxCount)
                .Select(a => $"{a.Title} by {a.Artist?.Name}")
                .ToList();
        }

        private Dictionary<string, int> CalculateDecadeDistribution(List<Album> albums)
        {
            var distribution = new Dictionary<string, int>();

            foreach (var album in albums.Where(a => a.ReleaseDate.HasValue))
            {
                var decade = (album.ReleaseDate.Value.Year / 10) * 10;
                var decadeKey = $"{decade}s";

                if (!distribution.ContainsKey(decadeKey))
                    distribution[decadeKey] = 0;

                distribution[decadeKey]++;
            }

            return distribution.OrderBy(kvp => kvp.Key).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private List<string> DeterminePreferredStyles(List<Artist> artists, List<Album> albums)
        {
            var styleCounts = new Dictionary<string, int>();

            foreach (var genre in ExtractGenres(artists, albums))
            {
                var baseStyle = ExtractBaseStyle(genre);
                if (!styleCounts.ContainsKey(baseStyle))
                    styleCounts[baseStyle] = 0;
                styleCounts[baseStyle]++;
            }

            return styleCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        private string ExtractBaseStyle(string genre)
        {
            if (genre.Contains("Rock")) return "Rock";
            if (genre.Contains("Metal")) return "Metal";
            if (genre.Contains("Pop")) return "Pop";
            if (genre.Contains("Jazz")) return "Jazz";
            if (genre.Contains("Electronic")) return "Electronic";
            if (genre.Contains("Hip") || genre.Contains("Rap")) return "Hip-Hop";
            if (genre.Contains("Country")) return "Country";
            if (genre.Contains("Classical")) return "Classical";
            return genre;
        }

        private void EnrichProfileWithDeepAnalysis(
            LibraryProfile profile,
            List<Artist> artists,
            List<Album> albums)
        {
            profile.ListeningPatterns = _libraryAnalyzer.AnalyzeListeningPatterns(artists, albums);
            profile.DiversityScore = _libraryAnalyzer.CalculateDiversityScore(profile);
            profile.RecommendationSeeds = _libraryAnalyzer.GenerateRecommendationSeeds(profile);
            
            _logger.Debug($"Enriched profile with deep analysis: Diversity score = {profile.DiversityScore}");
        }
    }
}