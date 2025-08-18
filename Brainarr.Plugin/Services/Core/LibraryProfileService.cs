using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class LibraryProfileService : ILibraryProfileService
    {
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;
        private readonly Dictionary<string, CachedProfile> _profileCache;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);

        private class CachedProfile
        {
            public LibraryProfile Profile { get; set; }
            public DateTime CachedAt { get; set; }
        }

        public LibraryProfileService(
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
        {
            _artistService = artistService;
            _albumService = albumService;
            _logger = logger;
            _profileCache = new Dictionary<string, CachedProfile>();
        }

        public LibraryProfile GetLibraryProfile()
        {
            try
            {
                var artists = _artistService.GetAllArtists();
                var albums = _albumService.GetAllAlbums();

                if (!artists.Any())
                {
                    _logger.Info("No artists in library, returning empty profile");
                    return CreateEmptyProfile();
                }

                var profile = new LibraryProfile
                {
                    TopGenres = ExtractTopGenres(albums),
                    Artists = ExtractTopArtists(artists),
                    RecentAlbums = ExtractRecentAlbums(albums),
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count
                };

                profile.ListeningTrends = DetermineListeningTrends(profile);

                _logger.Info($"Generated library profile: {profile.TotalArtists} artists, {profile.TotalAlbums} albums");
                
                return profile;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to generate library profile");
                return CreateEmptyProfile();
            }
        }

        public string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            if (profile == null)
            {
                return "empty";
            }

            var components = new List<string>
            {
                string.Join(",", profile.TopGenres.Take(5).OrderBy(g => g)),
                profile.TotalArtists.ToString(),
                profile.TotalAlbums.ToString(),
                string.Join(",", profile.ListeningTrends.OrderBy(t => t)),
                profile.Artists.Count.ToString()
            };

            var fingerprint = string.Join("|", components);
            return Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(fingerprint))
                .Replace("/", "_")
                .Replace("+", "-")
                .Substring(0, Math.Min(32, fingerprint.Length));
        }

        public List<string> DetermineListeningTrends(LibraryProfile profile)
        {
            var trends = new List<string>();

            if (profile == null || !profile.TopGenres.Any())
            {
                return trends;
            }

            var genreAnalysis = AnalyzeGenres(profile.TopGenres);
            trends.AddRange(genreAnalysis);

            var collectionAnalysis = AnalyzeCollection(profile);
            trends.AddRange(collectionAnalysis);

            var artistAnalysis = AnalyzeArtists(profile.Artists);
            trends.AddRange(artistAnalysis);

            return trends.Distinct().ToList();
        }

        public LibraryProfile GetCachedProfile(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
            {
                return null;
            }

            lock (_profileCache)
            {
                if (_profileCache.TryGetValue(cacheKey, out var cached))
                {
                    if (DateTime.UtcNow - cached.CachedAt < _cacheExpiration)
                    {
                        _logger.Debug("Returning cached library profile");
                        return cached.Profile;
                    }
                    
                    _profileCache.Remove(cacheKey);
                }
            }

            return null;
        }

        public void CacheProfile(string cacheKey, LibraryProfile profile)
        {
            if (string.IsNullOrEmpty(cacheKey) || profile == null)
            {
                return;
            }

            lock (_profileCache)
            {
                _profileCache[cacheKey] = new CachedProfile
                {
                    Profile = profile,
                    CachedAt = DateTime.UtcNow
                };

                if (_profileCache.Count > 10)
                {
                    var oldestKey = _profileCache
                        .OrderBy(kvp => kvp.Value.CachedAt)
                        .First().Key;
                    _profileCache.Remove(oldestKey);
                }
            }
        }

        private List<string> ExtractTopGenres(List<Album> albums)
        {
            return albums
                .Where(a => a.Genres != null)
                .SelectMany(a => a.Genres)
                .GroupBy(g => g.ToLower())
                .OrderByDescending(g => g.Count())
                .Take(15)
                .Select(g => g.First())
                .ToList();
        }

        private List<ArtistProfile> ExtractTopArtists(List<Artist> artists)
        {
            return artists
                .OrderByDescending(a => a.Statistics?.AlbumCount ?? 0)
                .ThenByDescending(a => a.Ratings?.Value ?? 0)
                .Take(30)
                .Select(a => new ArtistProfile
                {
                    Name = a.Name,
                    Genres = a.Genres ?? new List<string>(),
                    AlbumCount = a.Statistics?.AlbumCount ?? 0,
                    Tags = a.Tags?.Select(t => t.Label).ToList() ?? new List<string>()
                })
                .ToList();
        }

        private List<AlbumProfile> ExtractRecentAlbums(List<Album> albums)
        {
            var cutoffDate = DateTime.UtcNow.AddMonths(-6);
            
            return albums
                .Where(a => a.Added > cutoffDate)
                .OrderByDescending(a => a.Added)
                .Take(30)
                .Select(a => new AlbumProfile
                {
                    Title = a.Title,
                    Artist = a.Artist?.Value?.Name ?? "Unknown Artist",
                    ReleaseDate = a.ReleaseDate,
                    Genres = a.Genres ?? new List<string>()
                })
                .ToList();
        }

        private List<string> AnalyzeGenres(List<string> genres)
        {
            var trends = new List<string>();
            var lowerGenres = genres.Select(g => g.ToLower()).ToList();

            if (lowerGenres.Any(g => g.Contains("metal") || g.Contains("hardcore")))
                trends.Add("Heavy Music Enthusiast");

            if (lowerGenres.Any(g => g.Contains("electronic") || g.Contains("techno") || g.Contains("house")))
                trends.Add("Electronic Music Fan");

            if (lowerGenres.Any(g => g.Contains("jazz") || g.Contains("blues")))
                trends.Add("Jazz & Blues Appreciator");

            if (lowerGenres.Any(g => g.Contains("classical") || g.Contains("orchestral")))
                trends.Add("Classical Music Listener");

            if (lowerGenres.Any(g => g.Contains("indie") || g.Contains("alternative")))
                trends.Add("Indie & Alternative Explorer");

            if (lowerGenres.Any(g => g.Contains("hip hop") || g.Contains("rap")))
                trends.Add("Hip Hop Head");

            if (lowerGenres.Count > 10)
                trends.Add("Eclectic Taste");

            return trends;
        }

        private List<string> AnalyzeCollection(LibraryProfile profile)
        {
            var trends = new List<string>();

            if (profile.TotalAlbums > 1000)
                trends.Add("Serious Collector");
            else if (profile.TotalAlbums > 500)
                trends.Add("Avid Collector");
            else if (profile.TotalAlbums > 100)
                trends.Add("Active Listener");

            if (profile.RecentAlbums.Count > 20)
                trends.Add("Frequent Discoverer");

            var recentGenres = profile.RecentAlbums
                .SelectMany(a => a.Genres)
                .Distinct()
                .Count();

            if (recentGenres > 5)
                trends.Add("Genre Explorer");

            return trends;
        }

        private List<string> AnalyzeArtists(List<ArtistProfile> artists)
        {
            var trends = new List<string>();

            if (artists.Any(a => a.AlbumCount > 10))
                trends.Add("Completionist");

            var avgAlbumsPerArtist = artists.Any() 
                ? artists.Average(a => a.AlbumCount) 
                : 0;

            if (avgAlbumsPerArtist > 5)
                trends.Add("Deep Diver");
            else if (avgAlbumsPerArtist < 2)
                trends.Add("Singles Explorer");

            return trends;
        }

        private LibraryProfile CreateEmptyProfile()
        {
            return new LibraryProfile
            {
                TopGenres = new List<string>(),
                Artists = new List<ArtistProfile>(),
                RecentAlbums = new List<AlbumProfile>(),
                TotalArtists = 0,
                TotalAlbums = 0,
                ListeningTrends = new List<string>()
            };
        }
    }
}