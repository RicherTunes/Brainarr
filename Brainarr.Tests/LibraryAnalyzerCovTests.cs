using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using Moq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Datastore;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests
{
    public class LibraryAnalyzerCovTests
    {
        private static int _nextArtistId;
        private readonly Mock<IArtistService> _artistService;
        private readonly Mock<IAlbumService> _albumService;
        private readonly Logger _logger;
        private readonly IStyleCatalogService _styleCatalog;

        public LibraryAnalyzerCovTests()
        {
            _artistService = new Mock<IArtistService>();
            _albumService = new Mock<IAlbumService>();
            _logger = TestLogger.CreateNullLogger();
            _styleCatalog = new StyleCatalogService(_logger, httpClient: null);
        }

        #region Constructor ArgumentNullException Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsArgumentNullException_WhenArtistServiceIsNull()
        {
            // source line 34
            var albumService = new Mock<IAlbumService>().Object;
            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var logger = _logger;

            var act = () => new LibraryAnalyzer(null!, albumService, styleCatalog, logger);

            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("artistService");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsArgumentNullException_WhenAlbumServiceIsNull()
        {
            // source line 35
            var artistService = new Mock<IArtistService>().Object;
            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var logger = _logger;

            var act = () => new LibraryAnalyzer(artistService, null!, styleCatalog, logger);

            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("albumService");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsArgumentNullException_WhenStyleCatalogIsNull()
        {
            // source line 36
            var artistService = new Mock<IArtistService>().Object;
            var albumService = new Mock<IAlbumService>().Object;
            var logger = _logger;

            var act = () => new LibraryAnalyzer(artistService, albumService, null!, logger);

            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("styleCatalog");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            // source line 37
            var artistService = new Mock<IArtistService>().Object;
            var albumService = new Mock<IAlbumService>().Object;
            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);

            var act = () => new LibraryAnalyzer(artistService, albumService, styleCatalog, null!);

            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("logger");
        }

        #endregion

        #region GetAllArtists/GetAllAlbums Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void GetAllArtists_ReturnsAllArtistsFromService()
        {
            // source line 44-46
            var artists = new List<Artist>
            {
                CreateArtist("Artist1"),
                CreateArtist("Artist2"),
                CreateArtist("Artist3")
            };
            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var result = analyzer.GetAllArtists();

            result.Should().HaveCount(3);
            result.Select(a => a.Name).Should().Contain(new[] { "Artist1", "Artist2", "Artist3" });
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetAllAlbums_ReturnsAllAlbumsFromService()
        {
            // source line 49-51
            var albums = new List<Album>
            {
                CreateAlbum("Album1"),
                CreateAlbum("Album2")
            };
            _artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var result = analyzer.GetAllAlbums();

            result.Should().HaveCount(2);
            result.Select(a => a.Title).Should().Contain(new[] { "Album1", "Album2" });
        }

        #endregion

        #region GetCollectionSize Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsStarterCollectionSize_WhenUnder50Artists()
        {
            // source line 357
            var artists = CreateTestArtists(25);
            var albums = CreateTestAlbums(50);

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionSize"].Should().Be("starter");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsGrowingCollectionSize_When50To199Artists()
        {
            // source line 358
            var artists = CreateTestArtists(100);
            var albums = CreateTestAlbums(300);

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionSize"].Should().Be("growing");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsEstablishedCollectionSize_When200To499Artists()
        {
            // source line 359
            var artists = CreateTestArtists(350);
            var albums = CreateTestAlbums(1000);

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionSize"].Should().Be("established");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsExtensiveCollectionSize_When500To999Artists()
        {
            // source line 360
            var artists = CreateTestArtists(750);
            var albums = CreateTestAlbums(2500);

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionSize"].Should().Be("extensive");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsMassiveCollectionSize_When1000OrMoreArtists()
        {
            // source line 361
            var artists = CreateTestArtists(1200);
            var albums = CreateTestAlbums(5000);

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionSize"].Should().Be("massive");
        }

        #endregion

        #region ExtractSecondaryTypes Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ExtractsSecondaryTypes_WhenAlbumsHaveSecondaryTypes()
        {
            // source line 329-342
            var artists = CreateTestArtists(3);
            var albums = new List<Album>
            {
                CreateAlbumWithSecondaryTypes("Album1", new[] { SecondaryAlbumType.Remix, SecondaryAlbumType.Live }),
                CreateAlbumWithSecondaryTypes("Album2", new[] { SecondaryAlbumType.Remix }),
                CreateAlbumWithSecondaryTypes("Album3", new[] { SecondaryAlbumType.Live, SecondaryAlbumType.Demo }),
                CreateAlbumWithSecondaryTypes("Album4", new[] { SecondaryAlbumType.Remix, SecondaryAlbumType.Live, SecondaryAlbumType.Demo })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata.Should().ContainKey("SecondaryTypes");
            var secondaryTypes = profile.Metadata["SecondaryTypes"] as List<string>;
            secondaryTypes.Should().NotBeNull();
            secondaryTypes.Should().Contain(new[] { "Remix", "Live" });
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_HandlesEmptySecondaryTypes_Gracefully()
        {
            var artists = CreateTestArtists(2);
            var albums = new List<Album>
            {
                CreateAlbum("Album1"),
                CreateAlbum("Album2")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata.Should().ContainKey("SecondaryTypes");
            var secondaryTypes = profile.Metadata["SecondaryTypes"] as List<string>;
            secondaryTypes.Should().NotBeNull().And.BeEmpty();
        }

        #endregion

        #region DetermineDiscoveryTrend Edge Cases

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsRapidlyExpanding_When30PercentRecentAdditions()
        {
            // source line 350
            var recentDate = DateTime.UtcNow.AddMonths(-3);
            var oldDate = DateTime.UtcNow.AddMonths(-12);

            var artists = new List<Artist>
            {
                CreateArtist("Recent1", added: recentDate),
                CreateArtist("Recent2", added: recentDate),
                CreateArtist("Recent3", added: recentDate),
                CreateArtist("Recent4", added: recentDate),
                CreateArtist("Old1", added: oldDate),
                CreateArtist("Old2", added: oldDate),
                CreateArtist("Old3", added: oldDate),
                CreateArtist("Old4", added: oldDate),
                CreateArtist("Old5", added: oldDate),
                CreateArtist("Old6", added: oldDate)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["DiscoveryTrend"].Should().Be("rapidly expanding");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsActivelyGrowing_When15To30PercentRecentAdditions()
        {
            // source line 351
            var recentDate = DateTime.UtcNow.AddMonths(-3);
            var oldDate = DateTime.UtcNow.AddMonths(-12);

            var artists = new List<Artist>
            {
                CreateArtist("Recent1", added: recentDate),
                CreateArtist("Recent2", added: recentDate),
                CreateArtist("Old1", added: oldDate),
                CreateArtist("Old2", added: oldDate),
                CreateArtist("Old3", added: oldDate),
                CreateArtist("Old4", added: oldDate),
                CreateArtist("Old5", added: oldDate),
                CreateArtist("Old6", added: oldDate),
                CreateArtist("Old7", added: oldDate),
                CreateArtist("Old8", added: oldDate)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["DiscoveryTrend"].Should().Be("actively growing");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsSteadyGrowth_When5To15PercentRecentAdditions()
        {
            // source line 352
            var recentDate = DateTime.UtcNow.AddMonths(-3);
            var oldDate = DateTime.UtcNow.AddMonths(-12);

            var artists = new List<Artist>
            {
                CreateArtist("Recent1", added: recentDate),
                CreateArtist("Old1", added: oldDate),
                CreateArtist("Old2", added: oldDate),
                CreateArtist("Old3", added: oldDate),
                CreateArtist("Old4", added: oldDate),
                CreateArtist("Old5", added: oldDate),
                CreateArtist("Old6", added: oldDate),
                CreateArtist("Old7", added: oldDate),
                CreateArtist("Old8", added: oldDate),
                CreateArtist("Old9", added: oldDate)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["DiscoveryTrend"].Should().Be("steady growth");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsStableCollection_WhenUnder5PercentRecentAdditions()
        {
            // source line 353
            var oldDate = DateTime.UtcNow.AddMonths(-12);

            var artists = new List<Artist>
            {
                CreateArtist("Old1", added: oldDate),
                CreateArtist("Old2", added: oldDate),
                CreateArtist("Old3", added: oldDate),
                CreateArtist("Old4", added: oldDate),
                CreateArtist("Old5", added: oldDate),
                CreateArtist("Old6", added: oldDate),
                CreateArtist("Old7", added: oldDate),
                CreateArtist("Old8", added: oldDate),
                CreateArtist("Old9", added: oldDate),
                CreateArtist("Old10", added: oldDate)
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["DiscoveryTrend"].Should().Be("stable collection");
        }

        #endregion

        #region CollectionDepthAnalysis Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsCompletionistStyle_WhenOver40PercentHaveManyAlbums()
        {
            // source line 278-280
            var artists = CreateTestArtists(10);
            var albums = new List<Album>();

            for (int artistId = 1; artistId <= 5; artistId++)
            {
                for (int i = 0; i < 6; i++)
                {
                    albums.Add(CreateAlbumWithArtistId($"Album{artistId}_{i}", artistId));
                }
            }
            for (int artistId = 6; artistId <= 10; artistId++)
            {
                albums.Add(CreateAlbumWithArtistId($"Album{artistId}_1", artistId));
            }

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionStyle"].Should().Be("Completionist - Collects full discographies");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsCasualStyle_WhenOver60PercentHaveFewAlbums()
        {
            // source line 282-284
            var artists = CreateTestArtists(10);
            var albums = new List<Album>();

            for (int artistId = 1; artistId <= 2; artistId++)
            {
                for (int i = 0; i < 6; i++)
                {
                    albums.Add(CreateAlbumWithArtistId($"Album{artistId}_{i}", artistId));
                }
            }
            for (int artistId = 3; artistId <= 10; artistId++)
            {
                albums.Add(CreateAlbumWithArtistId($"Album{artistId}_1", artistId));
            }

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionStyle"].Should().Be("Casual - Collects select albums");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsBalancedStyle_WhenNeitherThresholdMet()
        {
            // source line 286-288
            var artists = CreateTestArtists(10);
            var albums = new List<Album>();

            for (int artistId = 1; artistId <= 3; artistId++)
            {
                for (int i = 0; i < 6; i++)
                {
                    albums.Add(CreateAlbumWithArtistId($"Album{artistId}_{i}", artistId));
                }
            }
            for (int artistId = 4; artistId <= 7; artistId++)
            {
                for (int i = 0; i < 3; i++)
                {
                    albums.Add(CreateAlbumWithArtistId($"Album{artistId}_{i}", artistId));
                }
            }
            for (int artistId = 8; artistId <= 10; artistId++)
            {
                albums.Add(CreateAlbumWithArtistId($"Album{artistId}_1", artistId));
            }

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["CollectionStyle"].Should().Be("Balanced - Mix of deep and shallow collections");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsStudioAlbumsPreferred_WhenStudioAlbumsDominate()
        {
            // source line 291-292
            var artists = CreateTestArtists(3);
            var albums = new List<Album>
            {
                CreateAlbumWithType("Studio1", "Studio"),
                CreateAlbumWithType("Studio2", "Studio"),
                CreateAlbumWithType("Studio3", "Studio"),
                CreateAlbumWithType("Studio4", "Studio"),
                CreateAlbumWithType("Studio5", "Studio"),
                CreateAlbumWithType("Studio6", "Studio"),
                CreateAlbumWithType("Studio7", "Studio"),
                CreateAlbumWithType("Compilation1", "Compilation")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["PreferredAlbumType"].Should().Be("Studio Albums");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReportsMixedAlbumType_WhenCompilationsSignificant()
        {
            // source line 292
            var artists = CreateTestArtists(3);
            var albums = new List<Album>
            {
                CreateAlbumWithType("Studio1", "Studio"),
                CreateAlbumWithType("Studio2", "Studio"),
                CreateAlbumWithType("Compilation1", "Compilation"),
                CreateAlbumWithType("Compilation2", "Greatest Hits")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata["PreferredAlbumType"].Should().Be("Mixed");
        }

        #endregion

        #region DetermineCollectionFocus Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_DeterminesSpecializedFocus_WhenTopGenreOver50Percent()
        {
            // source line 372-373
            var artists = new List<Artist>
            {
                CreateArtistWithGenres("Artist1", new[] { "Rock" }),
                CreateArtistWithGenres("Artist2", new[] { "Rock" }),
                CreateArtistWithGenres("Artist3", new[] { "Rock" }),
                CreateArtistWithGenres("Artist4", new[] { "Rock" }),
                CreateArtistWithGenres("Artist5", new[] { "Rock" }),
                CreateArtistWithGenres("Artist6", new[] { "Jazz" }),
                CreateArtistWithGenres("Artist7", new[] { "Jazz" })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            var focus = profile.Metadata["CollectionFocus"].ToString();
            focus.Should().StartWith("specialized-");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_DeterminesFocusedGenre_WhenTopGenreOver30Percent()
        {
            // source line 374
            var artists = new List<Artist>
            {
                CreateArtistWithGenres("Artist1", new[] { "Rock" }),
                CreateArtistWithGenres("Artist2", new[] { "Rock" }),
                CreateArtistWithGenres("Artist3", new[] { "Rock" }),
                CreateArtistWithGenres("Artist4", new[] { "Jazz" }),
                CreateArtistWithGenres("Artist5", new[] { "Jazz" }),
                CreateArtistWithGenres("Artist6", new[] { "Electronic" }),
                CreateArtistWithGenres("Artist7", new[] { "Pop" })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            var focus = profile.Metadata["CollectionFocus"].ToString();
            focus.Should().StartWith("focused-");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_DeterminesEclecticFocus_WhenNoGenreDominates()
        {
            // source line 370
            var artists = new List<Artist>
            {
                CreateArtistWithGenres("Artist1", new[] { "Rock" }),
                CreateArtistWithGenres("Artist2", new[] { "Jazz" }),
                CreateArtistWithGenres("Artist3", new[] { "Electronic" }),
                CreateArtistWithGenres("Artist4", new[] { "Pop" }),
                CreateArtistWithGenres("Artist5", new[] { "Classical" }),
                CreateArtistWithGenres("Artist6", new[] { "Metal" }),
                CreateArtistWithGenres("Artist7", new[] { "Folk" })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            var focus = profile.Metadata["CollectionFocus"].ToString();
            focus.Should().StartWith("eclectic-");
        }

        #endregion

        #region Temporal Focus Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_DeterminesCurrentTemporalFocus_WhenHighNewReleaseRatio()
        {
            // source line 381-382
            var artists = CreateTestArtists(5);
            var currentYear = DateTime.UtcNow.Year;
            var albums = new List<Album>
            {
                CreateAlbumWithDate("Recent1", new DateTime(currentYear - 1, 6, 1)),
                CreateAlbumWithDate("Recent2", new DateTime(currentYear - 1, 8, 1)),
                CreateAlbumWithDate("Recent3", new DateTime(currentYear, 1, 1)),
                CreateAlbumWithDate("Recent4", new DateTime(currentYear, 2, 1)),
                CreateAlbumWithDate("Old1", new DateTime(2010, 1, 1)),
                CreateAlbumWithDate("Old2", new DateTime(2005, 1, 1)),
                CreateAlbumWithDate("Old3", new DateTime(1995, 1, 1))
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            var focus = profile.Metadata["CollectionFocus"].ToString();
            focus.Should().EndWith("-current");
        }

        #endregion

        #region CalculateGenreDiversity Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_CalculatesGenreDiversityScore_WithMultipleGenres()
        {
            // source line 418-433
            var artists = new List<Artist>
            {
                CreateArtistWithGenres("Artist1", new[] { "Rock" }),
                CreateArtistWithGenres("Artist2", new[] { "Rock" }),
                CreateArtistWithGenres("Artist3", new[] { "Jazz" }),
                CreateArtistWithGenres("Artist4", new[] { "Electronic" }),
                CreateArtistWithGenres("Artist5", new[] { "Pop" })
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata.Should().ContainKey("GenreDistribution");
            var distribution = profile.Metadata["GenreDistribution"] as Dictionary<string, double>;
            distribution.Should().NotBeNull();
            distribution.Should().ContainKey("genre_diversity_score");
            distribution.Should().ContainKey("dominant_genre_percentage");
            distribution.Should().ContainKey("genre_count");
        }

        #endregion

        #region GetRecentlyAddedArtists Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ReturnsRecentlyAddedArtists_InCorrectOrder()
        {
            // source line 449-456
            var now = DateTime.UtcNow;
            var artists = new List<Artist>
            {
                CreateArtist("Oldest", added: now.AddDays(-100)),
                CreateArtist("Middle", added: now.AddDays(-50)),
                CreateArtist("Newest", added: now.AddDays(-1)),
                CreateArtist("SecondNewest", added: now.AddDays(-5))
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.RecentlyAdded.Should().NotBeNull();
            profile.RecentlyAdded.Should().HaveCount(4);
            profile.RecentlyAdded.First().Should().Be("Newest");
        }

        #endregion

        #region TopCollectedArtists Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_IdentifiesTopCollectedArtists_WithCorrectCounts()
        {
            // source line 297-304
            var artists = CreateTestArtists(5);
            var albums = new List<Album>();

            for (int i = 0; i < 10; i++) albums.Add(CreateAlbumWithArtistId($"A1_Album{i}", 1));
            for (int i = 0; i < 7; i++) albums.Add(CreateAlbumWithArtistId($"A2_Album{i}", 2));
            for (int i = 0; i < 5; i++) albums.Add(CreateAlbumWithArtistId($"A3_Album{i}", 3));
            for (int i = 0; i < 2; i++) albums.Add(CreateAlbumWithArtistId($"A4_Album{i}", 4));
            for (int i = 0; i < 1; i++) albums.Add(CreateAlbumWithArtistId($"A5_Album{i}", 5));

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.Metadata.Should().ContainKey("TopCollectedArtists");
            var topCollected = profile.Metadata["TopCollectedArtists"] as List<ArtistDepth>;
            topCollected.Should().NotBeNull();
            topCollected.Should().HaveCount(5);
            topCollected[0].ArtistId.Should().Be(1);
            topCollected[0].AlbumCount.Should().Be(10);
            topCollected[0].IsComplete.Should().BeTrue();
        }

        #endregion

        #region LibraryAnalyzerOptions Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_UsesDefaultOptions_WhenOptionsIsNull()
        {
            // source line 38
            _artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger, null);

            var profile = analyzer.AnalyzeLibrary();
            profile.Should().NotBeNull();
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Constructor_UsesProvidedOptions_WhenOptionsAreSpecified()
        {
            // source line 38
            var options = new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = false,
                ParallelizationThreshold = 32,
                MaxDegreeOfParallelism = 2
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger, options);

            var profile = analyzer.AnalyzeLibrary();
            profile.Should().NotBeNull();
        }

        #endregion

        #region ExtractGenresFromOverviews Tests

        [Fact]
        [Trait("Category", "Unit")]
        public void AnalyzeLibrary_ExtractsGenresFromOverviews_WhenNoDirectGenreData()
        {
            // source line 175-198
            var artists = new List<Artist>
            {
                CreateArtistWithOverview("Artist1", "This artist plays rock and roll music"),
                CreateArtistWithOverview("Artist2", "A pioneer of electronic music"),
                CreateArtistWithOverview("Artist3", "Jazz and blues influences")
            };

            _artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            _albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var analyzer = new LibraryAnalyzer(_artistService.Object, _albumService.Object, _styleCatalog, _logger);

            var profile = analyzer.AnalyzeLibrary();

            profile.TopGenres.Should().NotBeEmpty();
        }

        #endregion

        #region Helper Methods

        private Artist CreateArtist(string name, bool monitored = true, DateTime? added = null)
        {
            var metadata = new NzbDrone.Core.Datastore.LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = name });
            return new Artist
            {
                Id = Interlocked.Increment(ref _nextArtistId),
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

        private Artist CreateArtistWithOverview(string name, string overview)
        {
            var artist = CreateArtist(name);
            artist.Metadata.Value.Overview = overview;
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

        private Album CreateAlbumWithArtistId(string title, int artistId)
        {
            return new Album
            {
                Title = title,
                ArtistId = artistId,
                AlbumType = "Album",
                Monitored = true
            };
        }

        private Album CreateAlbumWithType(string title, string albumType)
        {
            return new Album
            {
                Title = title,
                AlbumType = albumType,
                ArtistId = 1,
                Monitored = true
            };
        }

        private Album CreateAlbumWithDate(string title, DateTime releaseDate)
        {
            var album = CreateAlbum(title);
            album.ReleaseDate = releaseDate;
            return album;
        }

        private Album CreateAlbumWithSecondaryTypes(string title, SecondaryAlbumType[] secondaryTypes)
        {
            var album = CreateAlbum(title);
            album.SecondaryTypes = secondaryTypes.ToList();
            return album;
        }

        private List<Artist> CreateTestArtists(int count)
        {
            var artists = new List<Artist>();
            for (int i = 1; i <= count; i++)
            {
                var artist = CreateArtist($"Artist{i}");
                artist.Id = i;
                artists.Add(artist);
            }
            return artists;
        }

        private List<Album> CreateTestAlbums(int count)
        {
            var albums = new List<Album>();
            for (int i = 1; i <= count; i++)
            {
                albums.Add(CreateAlbum($"Album{i}"));
            }
            return albums;
        }

        #endregion
    }
}
