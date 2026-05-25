using System;
using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Collections;
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

        // Wave 18D-T3: bounded TTL cache via Common's BoundedConcurrentDictionary.
        // Was an unbounded ConcurrentDictionary that grew per distinct cache key.
        // BCD clears all on capacity reach (cheap POCO entries, no disposable resources).
        // TTL check remains manual since BCD doesn't expose per-entry expiry.
        private readonly BoundedConcurrentDictionary<string, (LibraryProfile Profile, DateTime CachedAt)> _cache;
        private readonly LibraryProfileOptions _options = new LibraryProfileOptions();

        public LibraryProfileService(ILibraryContextBuilder builder, Logger logger, IArtistService artistService, IAlbumService albumService, LibraryProfileOptions? options = null)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _artistService = artistService;
            _albumService = albumService;
            _options = options ?? new LibraryProfileOptions();
            _cache = new BoundedConcurrentDictionary<string, (LibraryProfile, DateTime)>(_options.MaxCapacity);
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
            if (profile == null)
            {
                return new List<string>();
            }

            var trends = new List<string>();

            // High-level library shape tags (stable across runs; safe for prompts/UI)
            if (profile.TotalAlbums >= 500)
            {
                trends.Add("Avid Collector");
            }

            var genreCount = profile.TopGenres?.Count ?? 0;
            if (genreCount >= 6)
            {
                trends.Add("Genre Explorer");
            }

            // Add a small amount of concrete context if available
            if (profile.RecentlyAdded?.Count > 0)
            {
                trends.AddRange(profile.RecentlyAdded.Take(5));
            }
            else if (profile.TopArtists?.Count > 0)
            {
                trends.AddRange(profile.TopArtists.Take(5));
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
