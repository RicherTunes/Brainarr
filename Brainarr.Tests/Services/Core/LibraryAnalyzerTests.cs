using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Datastore;

namespace Brainarr.Tests.Services.Core
{
    public class LibraryAnalyzerTests
    {
        private readonly Mock<IArtistService> _artistService;
        private readonly Mock<IAlbumService> _albumService;
        private readonly Logger _logger;
        private readonly LibraryAnalyzer _analyzer;
        private readonly IStyleCatalogService _styleCatalog;

        public LibraryAnalyzerTests()
        {
            _artistService = new Mock<IArtistService>();
            _albumService = new Mock<IAlbumService>();
            _logger = TestLogger.CreateNullLogger();
            _styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            _analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ExtractsRealGenresFromMetadata()
        {
            // Arrange
            var artists = new List<Artist>
            {
                CreateArtistWithGenres("Artist1", new[] { "Rock", "Alternative" }),
                CreateArtistWithGenres("Artist2", new[] { "Electronic", "Rock" }),
                CreateArtistWithGenres("Artist3", new[] { "Jazz", "Blues" })
            };

            var albums = new List<Album>
            {
                CreateAlbumWithGenres("Album1", new[] { "Rock", "Indie" }),
                CreateAlbumWithGenres("Album2", new[] { "Electronic" })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.TopGenres.Should().ContainKey("Rock");
            profile.TopGenres["Rock"].Should().BeGreaterThan(2); // Should be most common
            profile.TopGenres.Should().ContainKey("Electronic");
            profile.TopGenres.Should().ContainKey("Jazz");
            profile.Metadata.Should().ContainKey("GenreDistribution");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_CalculatesTemporalPatterns()
        {
            // Arrange
            var artists = CreateTestArtists(5);
            var albums = new List<Album>
            {
                CreateAlbumWithDate("Album1", new DateTime(1975, 1, 1)),
                CreateAlbumWithDate("Album2", new DateTime(1975, 6, 1)),
                CreateAlbumWithDate("Album3", new DateTime(1985, 1, 1)),
                CreateAlbumWithDate("Album4", new DateTime(1995, 1, 1)),
                CreateAlbumWithDate("Album5", new DateTime(2010, 1, 1)),
                CreateAlbumWithDate("Album6", new DateTime(2023, 1, 1)),
                CreateAlbumWithDate("Album7", new DateTime(2024, 1, 1))
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("ReleaseDecades");
            var decades = profile.Metadata["ReleaseDecades"] as List<string>;
            decades.Should().Contain("1970s");
            decades.Count.Should().BeLessThanOrEqualTo(3);

            profile.Metadata.Should().ContainKey("NewReleaseRatio");
            var newReleaseRatio = (double)profile.Metadata["NewReleaseRatio"];
            newReleaseRatio.Should().BeGreaterThan(0);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_CalculatesCollectionQualityMetrics()
        {
            // Arrange
            var artists = new List<Artist>
            {
                CreateArtist("Artist1", monitored: true),
                CreateArtist("Artist2", monitored: true),
                CreateArtist("Artist3", monitored: false)
            };

            var albums = new List<Album>
            {
                CreateAlbum("Album1", monitored: true),
                CreateAlbum("Album2", monitored: true),
                CreateAlbum("Album3", monitored: true),
                CreateAlbum("Album4", monitored: false)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("MonitoredRatio");
            var monitoredRatio = (double)profile.Metadata["MonitoredRatio"];
            monitoredRatio.Should().BeApproximately(0.667, 0.01);

            profile.Metadata.Should().ContainKey("CollectionCompleteness");
            var completeness = (double)profile.Metadata["CollectionCompleteness"];
            completeness.Should().Be(0.75);

            profile.Metadata.Should().ContainKey("AverageAlbumsPerArtist");
            var avgAlbums = (double)profile.Metadata["AverageAlbumsPerArtist"];
            avgAlbums.Should().BeApproximately(1.33, 0.01);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_DeterminesDiscoveryTrend()
        {
            // Arrange
            var recentDate = DateTime.UtcNow.AddMonths(-3);
            var oldDate = DateTime.UtcNow.AddYears(-2);

            var artists = new List<Artist>
            {
                CreateArtist("Artist1", added: recentDate),
                CreateArtist("Artist2", added: recentDate),
                CreateArtist("Artist3", added: recentDate),
                CreateArtist("Artist4", added: oldDate),
                CreateArtist("Artist5", added: oldDate)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("DiscoveryTrend");
            var trend = profile.Metadata["DiscoveryTrend"].ToString();
            trend.Should().Be("rapidly expanding");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_IdentifiesAlbumTypePreferences()
        {
            // Arrange
            var artists = CreateTestArtists(2);
            var albums = new List<Album>
            {
                CreateAlbum("Album1", albumType: "Album"),
                CreateAlbum("Album2", albumType: "Album"),
                CreateAlbum("Album3", albumType: "Album"),
                CreateAlbum("EP1", albumType: "EP"),
                CreateAlbum("Single1", albumType: "Single")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("AlbumTypes");
            var albumTypes = profile.Metadata["AlbumTypes"] as Dictionary<string, int>;
            albumTypes.Should().ContainKey("Album");
            albumTypes["Album"].Should().Be(3);
            albumTypes.Should().ContainKey("EP");
            albumTypes["EP"].Should().Be(1);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void BuildPrompt_IncludesEnhancedContext()
        {
            // Arrange
            var profile = new LibraryProfile
            {
                TotalArtists = 100,
                TotalAlbums = 500,
                TopGenres = new Dictionary<string, int> { { "Rock", 50 }, { "Jazz", 30 } },
                TopArtists = new List<string> { "Artist1", "Artist2" },
                Metadata = new Dictionary<string, object>
                {
                    ["CollectionSize"] = "established",
                    ["CollectionFocus"] = "focused-classic",
                    ["ReleaseDecades"] = new List<string> { "1970s", "1980s" },
                    ["DiscoveryTrend"] = "steady growth",
                    ["MonitoredRatio"] = 0.85,
                    ["CollectionCompleteness"] = 0.75,
                    ["NewReleaseRatio"] = 0.1
                }
            };

            // Act
            var prompt = _analyzer.BuildPrompt(profile, 10, NzbDrone.Core.ImportLists.Brainarr.DiscoveryMode.Adjacent);

            // Assert
            prompt.Should().Contain("COLLECTION OVERVIEW");
            prompt.Should().Contain("MUSICAL PREFERENCES");
            prompt.Should().Contain("COLLECTION QUALITY");
            prompt.Should().Contain("established");
            prompt.Should().Contain("focused-classic");
            prompt.Should().Contain("1970s");
            prompt.Should().Contain("steady growth");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void FilterDuplicates_DecodesHtmlEntitiesBeforeMatching()
        {
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "AC/DC & Friends" }
            };

            var albums = new List<Album>();

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "AC/DC &amp; Friends", Album = string.Empty }
            };

            var filtered = _analyzer.FilterDuplicates(recommendations);

            filtered.Should().BeEmpty("HTML-encoded ampersands should not bypass duplicate detection");
        }
        [Fact]
        [Trait("Category", "Unit")]
        public void FilterDuplicates_UsesEnhancedMatching()
        {
            // Arrange
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "The Beatles" },
                new Artist { Id = 2, Name = "Pink Floyd" }
            };

            var albums = new List<Album>
            {
                new Album { ArtistId = 1, Title = "Abbey Road" },
                new Album { ArtistId = 2, Title = "Dark Side of the Moon" }
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var recommendations = new List<ImportListItemInfo>
            {
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road" }, // Duplicate
                new ImportListItemInfo { Artist = "Beatles", Album = "Abbey Road" }, // Duplicate without "The"
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV" }, // New
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall" } // New
            };

            // Act
            var filtered = _analyzer.FilterDuplicates(recommendations);

            // Assert
            filtered.Should().HaveCount(2);
            filtered.Should().NotContain(r => r.Album == "Abbey Road");
            filtered.Should().Contain(r => r.Artist == "Led Zeppelin");
            filtered.Should().Contain(r => r.Album == "The Wall");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_HandlesMissingGenresGracefully()
        {
            // Arrange
            var artists = CreateTestArtists(3); // No genres
            var albums = new List<Album>
            {
                CreateAlbum("Album1"),
                CreateAlbum("Album2")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.TopGenres.Should().NotBeEmpty();
            profile.TopGenres.Should().ContainKey("Rock"); // Should use fallback genres
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void AnalyzeLibrary_HandlesEmptyLibrary()
        {
            // Arrange
            _artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.TotalArtists.Should().Be(0);
            profile.TotalAlbums.Should().Be(0);
            profile.Metadata["DiscoveryTrend"].Should().Be("new collection");
            profile.Metadata["CollectionSize"].Should().Be("starter");
        }

        [Fact]
        [Trait("Category", "EdgeCase")]
        public void AnalyzeLibrary_HandlesServiceFailure()
        {
            // Arrange
            _artistService.Setup(s => s.GetAllArtists()).Throws(new Exception("Database error"));

            // Act
            var profile = _analyzer.AnalyzeLibrary();

            // Assert
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(100); // Fallback values
            profile.TotalAlbums.Should().Be(500);
            profile.TopGenres.Should().NotBeEmpty();
        }
        [Fact]
        public void AnalyzeLibrary_ShouldPopulateStyleContextCoverageAndIndex()
        {
            var styleCatalog = new Mock<IStyleCatalogService>();
            styleCatalog.Setup(x => x.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns<IEnumerable<string>>(values =>
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (values == null)
                    {
                        return set;
                    }

                    foreach (var value in values)
                    {
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        var slug = value.Trim().ToLowerInvariant().Replace(" ", "-");
                        set.Add(slug);
                    }

                    return set;
                });

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, styleCatalog.Object, _logger);

            var artist1 = CreateArtistWithGenres("Alpha", new[] { "Prog Rock", "Art Rock" });
            artist1.Id = 1;
            var artist2 = CreateArtistWithGenres("Beta", new[] { "Synthpop" });
            artist2.Id = 2;

            var artists = new List<Artist> { artist1, artist2 };

            var album1 = CreateAlbumWithGenres("Strict Album", new[] { "Prog Rock" });
            album1.Id = 10;
            album1.ArtistId = 1;

            var album2 = CreateAlbum("Fallback Album");
            album2.Id = 11;
            album2.ArtistId = 2;
            album2.Genres = new List<string>();

            var albums = new List<Album> { album1, album2 };

            _artistService.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumService.Setup(x => x.GetAllAlbums()).Returns(albums);

            var profile = analyzer.AnalyzeLibrary();
            var context = profile.StyleContext;

            context.Should().NotBeNull();
            context.HasStyles.Should().BeTrue();

            context.StyleCoverage.Should().ContainKey("prog-rock");
            context.StyleCoverage["prog-rock"].Should().Be(2);
            context.StyleCoverage["synthpop"].Should().Be(2);

            context.ArtistStyles[1].Should().BeEquivalentTo(new[] { "prog-rock", "art-rock" });
            context.AlbumStyles[11].Should().BeEquivalentTo(new[] { "synthpop" });

            context.StyleIndex.GetArtistsForStyles(new[] { "prog-rock" }).Should().Equal(new[] { 1 });
            context.StyleIndex.GetAlbumsForStyles(new[] { "synthpop" }).Should().Equal(new[] { 11 });
        }

        [Fact]
        public void AnalyzeLibrary_ShouldSupportParallelStyleContext()
        {
            var artists = new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Artist Parallel 1",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Genres = new List<string> { "Rock" } }),
                    Added = DateTime.UtcNow.AddDays(-10)
                },
                new Artist
                {
                    Id = 2,
                    Name = "Artist Parallel 2",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Genres = new List<string> { "Jazz" } }),
                    Added = DateTime.UtcNow.AddDays(-5)
                }
            };

            var albums = new List<Album>
            {
                new Album { Id = 10, ArtistId = 1, Title = "Rock Album", Genres = new List<string> { "Rock" } },
                new Album { Id = 20, ArtistId = 2, Title = "Jazz Album", Genres = new List<string> { "Jazz" } }
            };

            _artistService.Setup(x => x.GetAllArtists()).Returns(artists);
            _albumService.Setup(x => x.GetAllAlbums()).Returns(albums);

            var options = new LibraryAnalyzerOptions
            {
                ParallelizationThreshold = 1,
                MaxDegreeOfParallelism = 2
            };

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger, options);

            var profile = analyzer.AnalyzeLibrary();

            profile.StyleContext.Should().NotBeNull();
            profile.StyleContext.ArtistStyles.Should().ContainKey(1);
            profile.StyleContext.ArtistStyles.Should().ContainKey(2);
            profile.StyleContext.StyleIndex.ArtistsByStyle.Should().ContainKey("rock");
            profile.StyleContext.StyleIndex.ArtistsByStyle.Should().ContainKey("jazz");
        }
        // Helper methods
        private Artist CreateArtist(string name, bool monitored = true, DateTime? added = null)
        {
            var metadata = new NzbDrone.Core.Datastore.LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = name });
            return new Artist
            {
                Id = new Random().Next(1, 1000),
                Name = name,
                Monitored = monitored,
                Added = added ?? DateTime.UtcNow.AddMonths(-12),
                Metadata = metadata
            };
        }

        private Artist CreateArtistWithGenres(string name, string[] genres)
        {
            var artist = CreateArtist(name);
            artist.Metadata.Value.Genres = genres.ToList();
            return artist;
        }

        private Album CreateAlbum(string title, bool monitored = true, string albumType = "Album")
        {
            return new Album
            {
                Title = title,
                Monitored = monitored,
                AlbumType = albumType,
                ArtistId = 1
            };
        }

        private Album CreateAlbumWithGenres(string title, string[] genres)
        {
            var album = CreateAlbum(title);
            album.Genres = genres.ToList();
            return album;
        }

        private Album CreateAlbumWithDate(string title, DateTime releaseDate)
        {
            var album = CreateAlbum(title);
            album.ReleaseDate = releaseDate;
            return album;
        }

        private List<Artist> CreateTestArtists(int count)
        {
            var artists = new List<Artist>();
            for (int i = 1; i <= count; i++)
            {
                artists.Add(CreateArtist($"Artist{i}"));
            }
            return artists;
        }
    }
}
