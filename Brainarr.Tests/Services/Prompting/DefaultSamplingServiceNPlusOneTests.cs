using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Services.Core;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    /// <summary>
    /// Regression coverage for the live-observed OOM site: <c>DefaultSamplingService.SampleArtists</c>
    /// grouped ALL host-fetched albums by <see cref="Album.ArtistId"/> (a per-album
    /// <c>LazyLoaded&lt;Artist&gt;</c> deref -> per-row DB round trip), and <c>CreateSampleAlbum</c> /
    /// <c>ResolveArtistName</c> touched <c>Album.ArtistId</c> / <c>Album.Artist.Value</c> per sampled
    /// album. Both now resolve via the plain <c>ArtistMetadataId</c> column.
    /// </summary>
    [Trait("Category", "Performance")]
    public class DefaultSamplingServiceNPlusOneTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private sealed class StubContextPolicy : IContextPolicy
        {
            private readonly int _artistCount;
            private readonly int _albumCount;

            public StubContextPolicy(int artistCount, int albumCount)
            {
                _artistCount = artistCount;
                _albumCount = albumCount;
            }

            public int DetermineTargetArtistCount(int totalArtists, int tokenBudget) => _artistCount;
            public int DetermineTargetAlbumCount(int totalAlbums, int tokenBudget) => _albumCount;
        }

        private sealed class EmptyStyleCatalog : IStyleCatalogService
        {
            public IReadOnlyList<StyleEntry> GetAll() => Array.Empty<StyleEntry>();
            public IEnumerable<StyleEntry> Search(string query, int limit = 50) => Array.Empty<StyleEntry>();
            public ISet<string> Normalize(IEnumerable<string> selected) => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs, bool relaxParentMatch = false) => false;
            public string? ResolveSlug(string value) => value;
            public StyleEntry? GetBySlug(string slug) => null;
            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug) => Array.Empty<StyleSimilarity>();
            public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        }

        private static (List<Artist> Artists, List<Album> Albums, LazyLoadCounter Counter) BuildLibrary(int artistCount)
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>(artistCount);
            var albums = new List<Album>();

            var albumId = 1;
            for (var i = 0; i < artistCount; i++)
            {
                var metadataId = i + 1;
                var artist = new Artist
                {
                    Id = metadataId,
                    ArtistMetadataId = metadataId,
                    Name = $"Artist {metadataId}",
                    Added = DateTime.UtcNow.AddDays(-i)
                };
                artists.Add(artist);

                // Artist index 2 (metadataId 3) is the clear top collector (4 albums); others have 1.
                var count = metadataId == 3 ? 4 : 1;
                for (var j = 0; j < count; j++)
                {
                    albums.Add(new Album
                    {
                        Id = albumId++,
                        Title = $"Album {metadataId}-{j}",
                        ArtistMetadataId = metadataId,
                        Added = DateTime.UtcNow.AddDays(-albumId),
                        ReleaseDate = DateTime.UtcNow.AddYears(-1),
                        Genres = new List<string>(),
                        Artist = new RecordingArtistLazyLoaded(artist, counter)
                    });
                }
            }

            return (artists, albums, counter);
        }

        [Theory]
        [InlineData(20)]
        [InlineData(120)]
        public void Sample_GroupsAlbumCountsAndResolvesArtists_WithoutPerAlbumLazyLoad(int artistCount)
        {
            var (artists, albums, counter) = BuildLibrary(artistCount);

            var service = new DefaultSamplingService(Logger, new EmptyStyleCatalog(),
                new StubContextPolicy(artistCount, albums.Count));

            var sample = service.Sample(
                artists,
                albums,
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings { DiscoveryMode = DiscoveryMode.Similar },
                tokenBudget: 8000,
                seed: 42,
                token: CancellationToken.None);

            // 0 => neither the SampleArtists album-count grouping nor CreateSampleAlbum/ResolveArtistName
            // dereferenced Album.ArtistId / Album.Artist.Value.
            Assert.Equal(0, counter.Count);

            // Album-count grouping preserved: metadataId 3 (Artist 3, 4 albums) is the top collector,
            // so with equal (zero) style scores it sorts first by the album-count tiebreak.
            Assert.NotEmpty(sample.Artists);
            Assert.Equal(3, sample.Artists[0].ArtistId);

            // CreateSampleAlbum resolved each sampled album's artist id + name from ArtistMetadataId.
            Assert.All(sample.Albums, a =>
            {
                Assert.Equal($"Artist {a.ArtistId}", a.ArtistName);
                Assert.NotEqual(0, a.ArtistId);
            });
        }
    }
}
