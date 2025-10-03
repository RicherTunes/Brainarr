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
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

        public LibraryProfileService(ILibraryContextBuilder builder, Logger logger, IArtistService artistService, IAlbumService albumService)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService;
            _albumService = albumService;
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
            // Prefer recently added; fall back to top artists
            if (profile?.RecentlyAdded != null && profile.RecentlyAdded.Count > 0)
            {
                return profile.RecentlyAdded.Take(10).ToList();
            }
            return profile?.TopArtists?.Take(10).ToList() ?? new List<string>();
        }

        public LibraryProfile GetCachedProfile(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (DateTime.UtcNow - entry.CachedAt < DefaultTtl)
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
