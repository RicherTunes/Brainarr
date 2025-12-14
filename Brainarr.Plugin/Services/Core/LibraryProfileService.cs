using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public sealed class LibraryProfileService : ILibraryProfileService
    {
        private readonly ILibraryContextBuilder _builder;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, (LibraryProfile Profile, DateTime CachedAt)> _cache = new();
        private readonly LibraryProfileOptions _options = new LibraryProfileOptions();

        public LibraryProfileService(ILibraryContextBuilder builder, Logger logger, IArtistService artistService, IAlbumService albumService, LibraryProfileOptions? options = null)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService;
            _albumService = albumService;
            _options = options ?? new LibraryProfileOptions();
        }

        public LibraryProfile GetLibraryProfile()
        {
            try
            {
                if (_artistService == null || _albumService == null)
                {
                    // Fallback: minimal profile when services are unavailable (rare outside DI)
                    return new LibraryProfile
                    {
                        TotalArtists = 0,
                        TotalAlbums = 0,
                        TopGenres = new Dictionary<string, int>(),
                        TopArtists = new List<string>(),
                        RecentlyAdded = new List<string>()
                    };
                }
                return _builder.BuildProfile(_artistService, _albumService);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "LibraryProfileService: failed BuildProfile; returning empty profile");
                return new LibraryProfile
                {
                    TotalArtists = 0,
                    TotalAlbums = 0,
                    TopGenres = new Dictionary<string, int>(),
                    TopArtists = new List<string>(),
                    RecentlyAdded = new List<string>()
                };
            }
        }

        public string GenerateLibraryFingerprint(LibraryProfile profile)
        {
            return _builder.GenerateFingerprint(profile);
        }

        public List<string> DetermineListeningTrends(LibraryProfile profile)
        {
            var trends = new List<string>();
            if (profile == null)
            {
                return trends;
            }

            // Library size tags
            if (profile.TotalAlbums > 1000)
            {
                trends.Add("Large Collection");
            }
            else if (profile.TotalAlbums > 500)
            {
                trends.Add("Avid Collector");
            }
            else if (profile.TotalAlbums > 100)
            {
                trends.Add("Active Listener");
            }

            // Genre diversity tags
            if (profile.TopGenres != null && profile.TopGenres.Count > 5)
            {
                trends.Add("Genre Explorer");
            }

            // Recent activity tags
            if (profile.RecentlyAdded != null && profile.RecentlyAdded.Count > 20)
            {
                trends.Add("Frequent Discoverer");
            }

            return trends;
        }

        public LibraryProfile GetCachedProfile(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAt < _options.Ttl)
                {
                    return entry.Profile;
                }
                _cache.TryRemove(cacheKey, out _);
            }
            return null;
        }

        public void CacheProfile(string cacheKey, LibraryProfile profile)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || profile == null) return;
            _cache[cacheKey] = (profile, DateTime.UtcNow);
        }
    }
}
