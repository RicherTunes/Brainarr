using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public void AnalyzeLibrary_CalculatesTemporalPatterns()
        {
            // Arrange
            var artists = CreateTestArtists(5);
            // Use dynamic dates relative to current year to avoid date-dependent test failures
            var currentYear = DateTime.UtcNow.Year;
            var albums = new List<Album>
            {
                CreateAlbumWithDate("Album1", new DateTime(1975, 1, 1)),
                CreateAlbumWithDate("Album2", new DateTime(1975, 6, 1)),
                CreateAlbumWithDate("Album3", new DateTime(1985, 1, 1)),
                CreateAlbumWithDate("Album4", new DateTime(1995, 1, 1)),
                CreateAlbumWithDate("Album5", new DateTime(2010, 1, 1)),
                CreateAlbumWithDate("Album6", new DateTime(currentYear - 1, 6, 1)),  // Last year
                CreateAlbumWithDate("Album7", new DateTime(currentYear, 1, 1))        // This year
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
        [Trait("Area", "EdgeCase")]
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
        [Trait("Area", "EdgeCase")]
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

        [Fact]
        public void PopulateStyleContext_ShouldMatchSequentialAndParallelRuns()
        {
            var styleCatalog = CreateSlugNormalizingCatalog();

            var baseArtists = new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Alpha",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Name = "Alpha",
                        Genres = new List<string> { "Dream Pop", "Shoegaze" }
                    })
                },
                new Artist
                {
                    Id = 2,
                    Name = "Beta",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Name = "Beta",
                        Genres = new List<string>()
                    })
                },
                new Artist
                {
                    Id = 3,
                    Name = "Gamma",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Name = "Gamma",
                        Genres = new List<string> { "Art Rock" }
                    })
                }
            };

            var baseAlbums = new List<Album>
            {
                new Album { Id = 10, ArtistId = 1, Title = "Alpha Debut", Genres = new List<string> { "Dream Pop" } },
                new Album { Id = 11, ArtistId = 2, Title = "Beta Debut", Genres = new List<string>() },
                new Album { Id = 12, ArtistId = 2, Title = "Beta Synth", Genres = new List<string> { "Synth Pop" } },
                new Album { Id = 13, ArtistId = 3, Title = "Gamma Live", Genres = new List<string>() }
            };

            var sequentialAnalyzer = CreateAnalyzer(styleCatalog.Object, new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = false,
                ParallelizationThreshold = int.MaxValue
            },
            baseArtists,
            baseAlbums);

            var parallelAnalyzer = CreateAnalyzer(styleCatalog.Object, new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = true,
                ParallelizationThreshold = 1,
                MaxDegreeOfParallelism = 4
            },
            baseArtists,
            baseAlbums);

            var sequentialContext = sequentialAnalyzer.AnalyzeLibrary().StyleContext;
            var parallelContext = parallelAnalyzer.AnalyzeLibrary().StyleContext;

            parallelContext.HasStyles.Should().Be(sequentialContext.HasStyles);
            parallelContext.StyleCoverage.Should().BeEquivalentTo(sequentialContext.StyleCoverage);
            parallelContext.AllStyleSlugs.Should().BeEquivalentTo(sequentialContext.AllStyleSlugs);
            parallelContext.DominantStyles.Should().Equal(sequentialContext.DominantStyles);

            parallelContext.ArtistStyles.Keys.Should().BeEquivalentTo(sequentialContext.ArtistStyles.Keys);
            foreach (var kvp in sequentialContext.ArtistStyles)
            {
                parallelContext.ArtistStyles[kvp.Key].Should().BeEquivalentTo(kvp.Value);
            }

            parallelContext.AlbumStyles.Keys.Should().BeEquivalentTo(sequentialContext.AlbumStyles.Keys);
            foreach (var kvp in sequentialContext.AlbumStyles)
            {
                parallelContext.AlbumStyles[kvp.Key].Should().BeEquivalentTo(kvp.Value);
            }

            parallelContext.StyleIndex.ArtistsByStyle.Should().ContainKeys(sequentialContext.StyleIndex.ArtistsByStyle.Keys);
            parallelContext.StyleIndex.AlbumsByStyle.Should().ContainKeys(sequentialContext.StyleIndex.AlbumsByStyle.Keys);

            foreach (var kvp in sequentialContext.StyleIndex.ArtistsByStyle)
            {
                parallelContext.StyleIndex.ArtistsByStyle[kvp.Key].Should().Equal(kvp.Value);
            }

            foreach (var kvp in sequentialContext.StyleIndex.AlbumsByStyle)
            {
                parallelContext.StyleIndex.AlbumsByStyle[kvp.Key].Should().Equal(kvp.Value);
            }
        }

        [Fact]
        public void PopulateStyleContextParallel_ShouldMatchAcrossParallelismLevels()
        {
            var callingThread = Thread.CurrentThread.ManagedThreadId;
            var styleCatalog = CreateSlugNormalizingCatalog();

            var artists = new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Parallel Artist One",
                    Metadata = new ThreadGuardedLazyLoaded<ArtistMetadata>(
                        callingThread,
                        new ArtistMetadata
                        {
                            Genres = new List<string> { "Dream Pop", "Synth Pop" }
                        })
                },
                new Artist
                {
                    Id = 2,
                    Name = "Parallel Artist Two",
                    Metadata = new ThreadGuardedLazyLoaded<ArtistMetadata>(
                        callingThread,
                        new ArtistMetadata
                        {
                            Genres = new List<string>()
                        })
                },
                new Artist
                {
                    Id = 3,
                    Name = "Parallel Artist Three",
                    Metadata = new ThreadGuardedLazyLoaded<ArtistMetadata>(
                        callingThread,
                        new ArtistMetadata
                        {
                            Genres = new List<string> { "Art Rock" }
                        })
                }
            };

            var albums = new List<Album>
            {
                new Album { Id = 10, ArtistId = 1, Title = "Own Styles", Genres = new List<string> { "Dream Pop" } },
                new Album { Id = 11, ArtistId = 2, Title = "Empty Styles", Genres = new List<string>() },
                new Album { Id = 12, ArtistId = 3, Title = "Fallback Styles", Genres = new List<string>() }
            };

            var artistService = new Mock<IArtistService>();
            artistService.Setup(x => x.GetAllArtists()).Returns(() => artists.Select(a =>
            {
                ArtistMetadata metadataValue = null;
                if (a.Metadata is ThreadGuardedLazyLoaded<ArtistMetadata> guarded)
                {
                    metadataValue = guarded.Value;
                }
                else if (a.Metadata != null)
                {
                    metadataValue = a.Metadata.Value;
                }

                ArtistMetadata clonedMetadata = null;
                if (metadataValue != null)
                {
                    clonedMetadata = new ArtistMetadata
                    {
                        Name = metadataValue.Name,
                        Overview = metadataValue.Overview,
                        Genres = metadataValue.Genres != null ? new List<string>(metadataValue.Genres) : null
                    };
                }

                return new Artist
                {
                    Id = a.Id,
                    Name = a.Name,
                    Added = a.Added,
                    Monitored = a.Monitored,
                    Metadata = clonedMetadata == null ? null : new ThreadGuardedLazyLoaded<ArtistMetadata>(callingThread, clonedMetadata)
                };
            }).ToList());

            var albumService = new Mock<IAlbumService>();
            albumService.Setup(x => x.GetAllAlbums()).Returns(() => albums.Select(a => new Album
            {
                Id = a.Id,
                ArtistId = a.ArtistId,
                Title = a.Title,
                Genres = a.Genres == null ? null : new List<string>(a.Genres),
                Added = a.Added,
                ReleaseDate = a.ReleaseDate,
                Monitored = a.Monitored
            }).ToList());

            var singleThreadAnalyzer = new LibraryAnalyzer(
                artistService.Object,
                albumService.Object,
                styleCatalog.Object,
                _logger,
                new LibraryAnalyzerOptions
                {
                    EnableParallelStyleContext = true,
                    ParallelizationThreshold = 1,
                    MaxDegreeOfParallelism = 1
                });

            var multiThreadAnalyzer = new LibraryAnalyzer(
                artistService.Object,
                albumService.Object,
                styleCatalog.Object,
                _logger,
                new LibraryAnalyzerOptions
                {
                    EnableParallelStyleContext = true,
                    ParallelizationThreshold = 1,
                    MaxDegreeOfParallelism = Math.Max(2, Math.Min(4, Environment.ProcessorCount))
                });

            var singleContext = singleThreadAnalyzer.AnalyzeLibrary().StyleContext;
            var multiContext = multiThreadAnalyzer.AnalyzeLibrary().StyleContext;

            multiContext.HasStyles.Should().Be(singleContext.HasStyles);
            multiContext.StyleCoverage.Should().BeEquivalentTo(singleContext.StyleCoverage);
            multiContext.AllStyleSlugs.Should().BeEquivalentTo(singleContext.AllStyleSlugs);
            multiContext.DominantStyles.Should().Equal(singleContext.DominantStyles);

            multiContext.ArtistStyles.Keys.Should().BeEquivalentTo(singleContext.ArtistStyles.Keys);
            foreach (var kvp in singleContext.ArtistStyles)
            {
                multiContext.ArtistStyles[kvp.Key].Should().BeEquivalentTo(kvp.Value);
            }

            multiContext.AlbumStyles.Keys.Should().BeEquivalentTo(singleContext.AlbumStyles.Keys);
            foreach (var kvp in singleContext.AlbumStyles)
            {
                multiContext.AlbumStyles[kvp.Key].Should().BeEquivalentTo(kvp.Value);
            }

            multiContext.StyleIndex.ArtistsByStyle.Should().ContainKeys(singleContext.StyleIndex.ArtistsByStyle.Keys);
            foreach (var kvp in singleContext.StyleIndex.ArtistsByStyle)
            {
                multiContext.StyleIndex.ArtistsByStyle[kvp.Key].Should().Equal(kvp.Value);
            }

            multiContext.StyleIndex.AlbumsByStyle.Should().ContainKeys(singleContext.StyleIndex.AlbumsByStyle.Keys);
            foreach (var kvp in singleContext.StyleIndex.AlbumsByStyle)
            {
                multiContext.StyleIndex.AlbumsByStyle[kvp.Key].Should().Equal(kvp.Value);
            }

            multiContext.AlbumStyles.Should().ContainKey(12);
            multiContext.AlbumStyles[12].Should().BeEquivalentTo(new[] { "art-rock" });
        }

        [Fact]
        public void PopulateStyleContext_ShouldNormalizeStylesOnCallingThread()
        {
            var callingThread = Thread.CurrentThread.ManagedThreadId;
            var styleCatalog = new ThreadGuardedStyleCatalog(callingThread);

            var artists = new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Thread Safe Artist",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Genres = new List<string> { "Dream Pop", "Art Rock" }
                    })
                },
                new Artist
                {
                    Id = 2,
                    Name = "Fallback Artist",
                    Metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata
                    {
                        Genres = new List<string>()
                    })
                }
            };

            var albums = new List<Album>
            {
                new Album { Id = 10, ArtistId = 1, Title = "Primary", Genres = new List<string> { "Dream Pop" } },
                new Album { Id = 11, ArtistId = 2, Title = "Fallback", Genres = new List<string>() }
            };

            var analyzer = CreateAnalyzer(
                styleCatalog,
                new LibraryAnalyzerOptions
                {
                    EnableParallelStyleContext = true,
                    ParallelizationThreshold = 1,
                    MaxDegreeOfParallelism = 4
                },
                artists,
                albums);

            var context = analyzer.AnalyzeLibrary().StyleContext;

            context.Should().NotBeNull();
            context.HasStyles.Should().BeTrue();

            var observedThreads = styleCatalog.ObservedThreads.ToArray();
            observedThreads.Should().NotBeEmpty();
            observedThreads.Should().OnlyContain(id => id == callingThread);
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

        private static Mock<IStyleCatalogService> CreateSlugNormalizingCatalog()
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

            return styleCatalog;
        }

        private sealed class ThreadGuardedLazyLoaded<T> : LazyLoaded<T>
        {
            private readonly int _allowedThreadId;

            public ThreadGuardedLazyLoaded(int allowedThreadId, T value)
                : base(value)
            {
                _allowedThreadId = allowedThreadId;
            }

            public override void LazyLoad()
            {
                var currentThread = Thread.CurrentThread.ManagedThreadId;
                if (currentThread != _allowedThreadId)
                {
                    throw new InvalidOperationException($"LazyLoad invoked on thread {currentThread}, expected {_allowedThreadId}.");
                }
            }
        }

        private sealed class ThreadGuardedStyleCatalog : IStyleCatalogService
        {
            private readonly int _allowedThreadId;
            private readonly List<int> _threads = new List<int>();
            private readonly object _lock = new object();

            public ThreadGuardedStyleCatalog(int allowedThreadId)
            {
                _allowedThreadId = allowedThreadId;
            }

            public IEnumerable<int> ObservedThreads
            {
                get
                {
                    lock (_lock)
                    {
                        return _threads.ToArray();
                    }
                }
            }

            public IReadOnlyList<StyleEntry> GetAll()
            {
                return Array.Empty<StyleEntry>();
            }

            public IEnumerable<StyleEntry> Search(string query, int limit = 50)
            {
                return Array.Empty<StyleEntry>();
            }

            public ISet<string> Normalize(IEnumerable<string> selected)
            {
                var currentThread = Thread.CurrentThread.ManagedThreadId;
                lock (_lock)
                {
                    _threads.Add(currentThread);
                }

                if (currentThread != _allowedThreadId)
                {
                    throw new InvalidOperationException($"Normalize invoked on thread {currentThread}, expected {_allowedThreadId}.");
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (selected == null)
                {
                    return set;
                }

                foreach (var value in selected)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var slug = value.Trim().ToLowerInvariant().Replace(" ", "-");
                    set.Add(slug);
                }

                return set;
            }

            public bool IsMatch(ICollection<string> libraryGenres, ISet<string> selectedStyleSlugs, bool relaxParentMatch = false)
            {
                return false;
            }

            public string? ResolveSlug(string value)
            {
                return value;
            }

            public StyleEntry? GetBySlug(string slug)
            {
                return null;
            }

            public IEnumerable<StyleSimilarity> GetSimilarSlugs(string slug)
            {
                return Array.Empty<StyleSimilarity>();
            }

            public Task RefreshAsync(CancellationToken token = default)
            {
                return Task.CompletedTask;
            }
        }

        private LibraryAnalyzer CreateAnalyzer(
            IStyleCatalogService styleCatalog,
            LibraryAnalyzerOptions options,
            List<Artist> artists,
            List<Album> albums)
        {
            var artistService = new Mock<IArtistService>();
            artistService.Setup(x => x.GetAllArtists()).Returns(() => CloneArtists(artists));

            var albumService = new Mock<IAlbumService>();
            albumService.Setup(x => x.GetAllAlbums()).Returns(() => CloneAlbums(albums));

            return new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, _logger, options);
        }

        private static List<Artist> CloneArtists(IEnumerable<Artist> source)
        {
            return source.Select(artist =>
            {
                var metadataValue = artist.Metadata?.Value;
                var clonedMetadata = metadataValue == null
                    ? null
                    : new ArtistMetadata
                    {
                        Name = metadataValue.Name,
                        Overview = metadataValue.Overview,
                        Genres = metadataValue.Genres?.ToList()
                    };

                return new Artist
                {
                    Id = artist.Id,
                    Name = artist.Name,
                    Added = artist.Added,
                    Monitored = artist.Monitored,
                    Metadata = clonedMetadata != null ? new LazyLoaded<ArtistMetadata>(clonedMetadata) : null
                };
            }).ToList();
        }

        private static List<Album> CloneAlbums(IEnumerable<Album> source)
        {
            return source.Select(album =>
            {
                var metadataValue = album.ArtistMetadata?.Value;
                var artistMetadata = metadataValue == null
                    ? null
                    : new ArtistMetadata
                    {
                        Name = metadataValue.Name,
                        Genres = metadataValue.Genres?.ToList(),
                        Overview = metadataValue.Overview
                    };

                return new Album
                {
                    Id = album.Id,
                    ArtistId = album.ArtistId,
                    Title = album.Title,
                    Genres = album.Genres != null ? new List<string>(album.Genres) : null,
                    Added = album.Added,
                    ReleaseDate = album.ReleaseDate,
                    Monitored = album.Monitored,
                    ArtistMetadata = artistMetadata
                };
            }).ToList();
        }
    }
}
