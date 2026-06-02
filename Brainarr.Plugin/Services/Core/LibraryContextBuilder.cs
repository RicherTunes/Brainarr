using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class LibraryContextBuilder : ILibraryContextBuilder
    {
        private readonly Logger _logger;
        private readonly StyleContextBuilder _styleContextBuilder;

        public LibraryContextBuilder(Logger logger, IStyleCatalogService styleCatalog = null)
        {
            _logger = logger;
            // Build the SAME slug-keyed StyleContext the prompt side uses (LibraryAnalyzer →
            // StyleContextBuilder) so the pipeline's post-validation style filter genre-first gate
            // (IsStyleSeededDiscovery) reflects real library coverage instead of always-empty. Reusing
            // StyleContextBuilder keeps a single source of truth — no divergent second implementation.
            // Optional for backwards compatibility: when no catalog is supplied (older callers/tests),
            // StyleContext stays empty and the filter degrades to genre-first (skip), exactly as before.
            _styleContextBuilder = styleCatalog != null
                ? new StyleContextBuilder(styleCatalog, new LibraryAnalyzerOptions(), logger)
                : null;
        }

        public LibraryProfile BuildProfile(IArtistService artistService, IAlbumService albumService)
        {
            try
            {
                var artists = artistService.GetAllArtists();
                var albums = albumService.GetAllAlbums();

                var artistAlbumCounts = albums
                    .GroupBy(a => a.ArtistId)
                    .Select(g => new { ArtistId = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(20)
                    .ToList();

                var topArtistNames = artistAlbumCounts
                    .Select(ac => artists.FirstOrDefault(a => a.Id == ac.ArtistId)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                // Create genre list (simplified since Lidarr doesn't expose genres directly)
                var genreCounts = new Dictionary<string, int>();
                for (int i = 0; i < Math.Min(5, BrainarrConstants.FallbackGenres.Length); i++)
                {
                    genreCounts[BrainarrConstants.FallbackGenres[i]] = 20 - (i * 3);
                }

                // Populate slug-keyed style coverage from real artist/album genre metadata so the
                // pipeline's post-validation style filter can engage (its genre-first gate keys off
                // StyleContext.StyleCoverage). When no catalog is wired the default empty context is
                // used (filter degrades to genre-first/skip). Failures are swallowed in StyleContextBuilder.
                var styleContext = _styleContextBuilder?.Build(artists, albums) ?? new LibraryStyleContext();

                return new LibraryProfile
                {
                    TotalArtists = artists.Count,
                    TotalAlbums = albums.Count,
                    TopGenres = genreCounts,
                    TopArtists = topArtistNames,
                    RecentlyAdded = artists
                        .OrderByDescending(a => a.Added)
                        .Take(10)
                        .Select(a => a.Name)
                        .ToList(),
                    StyleContext = styleContext
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to get real library data, using fallback: {ex.Message}");
                return GetFallbackProfile();
            }
        }

        public string GenerateFingerprint(LibraryProfile profile)
        {
            var topArtistsHash = string.Join(",", profile.TopArtists.Take(10)).GetHashCode();
            var topGenresHash = string.Join(",", profile.TopGenres.Take(5).Select(g => g.Key)).GetHashCode();
            var recentlyAddedHash = string.Join(",", profile.RecentlyAdded.Take(5)).GetHashCode();

            return $"{profile.TotalArtists}_{profile.TotalAlbums}_{Math.Abs(topArtistsHash)}_{Math.Abs(topGenresHash)}_{Math.Abs(recentlyAddedHash)}";
        }

        private LibraryProfile GetFallbackProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int>
                {
                    { "Rock", 30 },
                    { "Electronic", 20 },
                    { "Jazz", 15 }
                },
                TopArtists = new List<string>
                {
                    "Radiohead",
                    "Pink Floyd",
                    "Miles Davis"
                },
                RecentlyAdded = new List<string>()
            };
        }
    }
}
