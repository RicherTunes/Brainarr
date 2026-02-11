using System;
using System.Collections.Generic;
using System.Diagnostics;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Regression detector for the N+1 fix in GetTopArtistsByAlbumCount.
    /// Verifies that AnalyzeLibrary with 1000 artists / 5000 albums completes
    /// well under 5 seconds — the pre-fix O(n*m) version would exceed this.
    /// Not a strict perf gate; just a canary for algorithmic regression.
    /// </summary>
    public class LibraryAnalyzerPerfTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        [Trait("Category", "Perf")]
        public void AnalyzeLibrary_LargeDataset_CompletesUnderBudget()
        {
            const int artistCount = 1000;
            const int albumsPerArtist = 5;
            const int totalAlbums = artistCount * albumsPerArtist;
            const long budgetMs = 5000;

            var artists = new List<Artist>(artistCount);
            var albums = new List<Album>(totalAlbums);

            for (var i = 1; i <= artistCount; i++)
            {
                artists.Add(new Artist
                {
                    Id = i,
                    Name = $"Artist{i}",
                    Monitored = true,
                    Added = DateTime.UtcNow.AddDays(-i),
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Name = $"Artist{i}",
                        Genres = new List<string> { "Rock", "Alternative" }
                    })
                });

                for (var j = 1; j <= albumsPerArtist; j++)
                {
                    var albumId = (i - 1) * albumsPerArtist + j;
                    albums.Add(new Album
                    {
                        Id = albumId,
                        ArtistId = i,
                        Title = $"Album{albumId}",
                        Monitored = true,
                        AlbumType = "Album",
                        ReleaseDate = DateTime.UtcNow.AddYears(-j),
                        Added = DateTime.UtcNow.AddDays(-albumId)
                    });
                }
            }

            var artistService = new Mock<IArtistService>();
            artistService.Setup(x => x.GetAllArtists()).Returns(artists);

            var albumService = new Mock<IAlbumService>();
            albumService.Setup(x => x.GetAllAlbums()).Returns(albums);

            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var analyzer = new LibraryAnalyzer(
                artistService.Object,
                albumService.Object,
                styleCatalog,
                _logger,
                new LibraryAnalyzerOptions { EnableParallelStyleContext = false });

            var sw = Stopwatch.StartNew();
            var profile = analyzer.AnalyzeLibrary();
            sw.Stop();

            Assert.True(
                sw.ElapsedMilliseconds < budgetMs,
                $"AnalyzeLibrary({artistCount} artists, {totalAlbums} albums) took {sw.ElapsedMilliseconds}ms, " +
                $"exceeding {budgetMs}ms budget. Possible N+1 regression in GetTopArtistsByAlbumCount.");

            Assert.NotNull(profile);
            Assert.Equal(artistCount, profile.TotalArtists);
            Assert.Equal(totalAlbums, profile.TotalAlbums);
            Assert.NotEmpty(profile.TopArtists);
        }
    }
}
