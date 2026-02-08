using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;
using NzbDrone.Core.Datastore;
using Xunit;

namespace Brainarr.Tests.Characterization
{
    /// <summary>
    /// Characterization tests locking LibraryAnalyzer behavior at public seams.
    /// These tests capture current behavior so that future refactorings can be
    /// verified as behavior-preserving. Assertions are structured (field-by-field).
    /// </summary>
    [Trait("Area", "Characterization")]
    [Trait("Area", "LibraryAnalyzer")]
    public class LibraryAnalyzerCharacterizationTests
    {
        private static readonly Logger Logger = TestLogger.CreateNullLogger();

        // ─── AnalyzeLibrary Characterization ─────────────────────────────────

        [Fact]
        public void Profile_AllMetadataKeys_PopulatedForRichLibrary()
        {
            // Arrange: 20 artists with genres, 50 albums with genres and release dates
            var artists = new List<Artist>();
            var genres = new[] { "Rock", "Jazz", "Electronic", "Metal", "Pop" };
            for (int i = 1; i <= 20; i++)
            {
                artists.Add(CreateArtistWithGenres(i, $"Artist{i}", new[] { genres[(i - 1) % genres.Length] }));
            }

            var albums = new List<Album>();
            for (int i = 1; i <= 50; i++)
            {
                var artistId = ((i - 1) % 20) + 1;
                var releaseYear = 1980 + (i % 40);
                albums.Add(CreateAlbum(i, artistId, $"Album{i}",
                    genres: new[] { genres[(i - 1) % genres.Length] },
                    releaseDate: new DateTime(releaseYear, 6, 1)));
            }

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert: all 16 expected metadata keys are present
            var expectedKeys = new[]
            {
                "GenreDistribution", "ReleaseDecades", "PreferredEras", "NewReleaseRatio",
                "MonitoredRatio", "CollectionCompleteness", "AverageAlbumsPerArtist",
                "AlbumTypes", "SecondaryTypes", "DiscoveryTrend", "CollectionSize",
                "CollectionFocus", "CollectionStyle", "CompletionistScore",
                "PreferredAlbumType", "TopCollectedArtists"
            };

            foreach (var key in expectedKeys)
            {
                profile.Metadata.Should().ContainKey(key,
                    $"metadata should contain '{key}' for a rich library");
            }
        }

        [Fact]
        public void Profile_StyleContext_PopulatedWithArtistAndAlbumStyles()
        {
            // Arrange: artists and albums with genre data for style resolution
            var artists = new List<Artist>();
            for (int i = 1; i <= 10; i++)
            {
                artists.Add(CreateArtistWithGenres(i, $"StyleArtist{i}", new[] { "Rock", "Alternative" }));
            }

            var albums = new List<Album>();
            for (int i = 1; i <= 20; i++)
            {
                albums.Add(CreateAlbum(i, ((i - 1) % 10) + 1, $"StyleAlbum{i}",
                    genres: new[] { "Rock", "Indie" }));
            }

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            var ctx = profile.StyleContext;
            ctx.Should().NotBeNull();
            ctx.HasStyles.Should().BeTrue("StyleContext should have styles when genres are present");
            ctx.StyleCoverage.Should().NotBeEmpty("StyleCoverage should be non-empty");
            ctx.ArtistStyles.Should().NotBeEmpty("ArtistStyles should be populated");
            ctx.AlbumStyles.Should().NotBeEmpty("AlbumStyles should be populated");
            ctx.StyleIndex.Should().NotBeNull("StyleIndex should be populated");
        }

        [Fact]
        public void Profile_EmptyLibrary_ReturnsFallbackValues()
        {
            // Arrange
            var analyzer = CreateAnalyzer(new List<Artist>(), new List<Album>());

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            profile.TotalArtists.Should().Be(0);
            profile.TotalAlbums.Should().Be(0);
            profile.Metadata["DiscoveryTrend"].Should().Be("new collection");
            profile.Metadata["CollectionSize"].Should().Be("starter");
        }

        [Fact]
        public void Profile_ServiceFailure_ReturnsFallbackProfile()
        {
            // Arrange: IArtistService throws an exception
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            artistService.Setup(s => s.GetAllArtists()).Throws(new InvalidOperationException("DB unavailable"));
            albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());

            var styleCatalog = new StyleCatalogService(Logger, httpClient: null);
            var analyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, Logger);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert: fallback profile
            profile.TotalArtists.Should().Be(100);
            profile.TotalAlbums.Should().Be(500);
            profile.TopGenres.Should().NotBeEmpty("fallback profile should have fallback genres");
        }

        [Fact]
        public void Profile_GenreExtraction_PrefersDirectGenresOverFallback()
        {
            // Arrange: artists with explicit genres
            var artists = new List<Artist>
            {
                CreateArtistWithGenres(1, "MetalHead", new[] { "Death Metal", "Black Metal" }),
                CreateArtistWithGenres(2, "JazzCat", new[] { "Bebop", "Cool Jazz" }),
                CreateArtistWithGenres(3, "Rocker", new[] { "Hard Rock" })
            };

            var albums = new List<Album>
            {
                CreateAlbum(1, 1, "Brutal", genres: new[] { "Death Metal" }),
                CreateAlbum(2, 2, "Smooth", genres: new[] { "Cool Jazz" }),
                CreateAlbum(3, 3, "Loud", genres: new[] { "Hard Rock" })
            };

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert: TopGenres should come from artist/album metadata, not fallback
            profile.TopGenres.Keys.Should().Contain(
                k => k.Contains("Metal") || k.Contains("Jazz") || k.Contains("Rock"),
                "genres should be extracted from artist/album metadata, not fallback list");

            // Ensure fallback-only genres are not the sole content
            var fallbackOnly = new[] { "Electronic", "Pop", "Classical", "Hip Hop", "Country" };
            profile.TopGenres.Keys.Should().NotBeSubsetOf(fallbackOnly,
                "TopGenres should include directly extracted genres");
        }

        [Fact]
        public void Profile_TemporalAnalysis_CapturesDecadesAndRatio()
        {
            // Arrange: albums with various release dates
            var artists = new List<Artist>
            {
                CreateArtist(1, "TemporalArtist")
            };

            var albums = new List<Album>
            {
                CreateAlbum(1, 1, "70s Classic", releaseDate: new DateTime(1975, 1, 1)),
                CreateAlbum(2, 1, "80s Hit", releaseDate: new DateTime(1985, 6, 1)),
                CreateAlbum(3, 1, "90s Grunge", releaseDate: new DateTime(1994, 3, 1)),
                CreateAlbum(4, 1, "Recent", releaseDate: DateTime.UtcNow.AddMonths(-6)),
                CreateAlbum(5, 1, "Very Recent", releaseDate: DateTime.UtcNow.AddMonths(-3))
            };

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            var releaseDecades = profile.Metadata["ReleaseDecades"] as List<string>;
            releaseDecades.Should().NotBeNull();
            releaseDecades.Count.Should().BeLessOrEqualTo(3, "ReleaseDecades should have at most 3 entries");

            var newReleaseRatio = (double)profile.Metadata["NewReleaseRatio"];
            newReleaseRatio.Should().BeGreaterThan(0, "recent albums should produce a positive NewReleaseRatio");
        }

        [Fact]
        public void Profile_CollectionDepth_PopulatesCompletionistData()
        {
            // Arrange: some artists with many albums to trigger completionist detection
            var artists = new List<Artist>();
            var albums = new List<Album>();
            int albumId = 0;

            for (int i = 1; i <= 5; i++)
            {
                artists.Add(CreateArtist(i, $"ProlificArtist{i}"));
                // Give each artist 8 albums
                for (int j = 1; j <= 8; j++)
                {
                    albumId++;
                    albums.Add(CreateAlbum(albumId, i, $"Album{albumId}"));
                }
            }

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var profile = analyzer.AnalyzeLibrary();

            // Assert
            profile.Metadata.Should().ContainKey("CollectionStyle");
            profile.Metadata["CollectionStyle"].Should().NotBeNull();
            profile.Metadata["CollectionStyle"].ToString().Should().NotBeNullOrWhiteSpace();

            profile.Metadata.Should().ContainKey("CompletionistScore");
            ((double)profile.Metadata["CompletionistScore"]).Should().BeGreaterThan(0,
                "artists with 8 albums each should produce a positive CompletionistScore");

            profile.Metadata.Should().ContainKey("PreferredAlbumType");
            profile.Metadata["PreferredAlbumType"].ToString().Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void Profile_ParallelAndSequential_ProduceSameResults()
        {
            // Arrange: enough data to trigger parallel path
            var artists = new List<Artist>();
            var albums = new List<Album>();
            for (int i = 1; i <= 40; i++)
            {
                artists.Add(CreateArtistWithGenres(i, $"Artist{i}", new[] { "Rock", "Alternative" }));
            }
            for (int i = 1; i <= 80; i++)
            {
                albums.Add(CreateAlbum(i, ((i - 1) % 40) + 1, $"Album{i}",
                    genres: new[] { "Rock", "Indie" }));
            }

            var parallelOptions = new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = true,
                ParallelizationThreshold = 1 // Force parallel
            };
            var sequentialOptions = new LibraryAnalyzerOptions
            {
                EnableParallelStyleContext = false
            };

            var parallelAnalyzer = CreateAnalyzer(artists, albums, parallelOptions);
            var sequentialAnalyzer = CreateAnalyzer(artists, albums, sequentialOptions);

            // Act
            var parallelProfile = parallelAnalyzer.AnalyzeLibrary();
            var sequentialProfile = sequentialAnalyzer.AnalyzeLibrary();

            // Assert: core fields should match
            parallelProfile.TotalArtists.Should().Be(sequentialProfile.TotalArtists);
            parallelProfile.TotalAlbums.Should().Be(sequentialProfile.TotalAlbums);

            // StyleContext: same slugs (order may differ)
            var parallelSlugs = parallelProfile.StyleContext.AllStyleSlugs.OrderBy(s => s).ToList();
            var sequentialSlugs = sequentialProfile.StyleContext.AllStyleSlugs.OrderBy(s => s).ToList();
            parallelSlugs.Should().BeEquivalentTo(sequentialSlugs,
                "parallel and sequential should produce equivalent style slugs");

            // Same HasStyles flag
            parallelProfile.StyleContext.HasStyles.Should().Be(sequentialProfile.StyleContext.HasStyles);
        }

        [Fact]
        public void StyleContextBuilder_ParallelAndSequential_ProduceSameResults()
        {
            // Arrange: enough data to trigger parallel path, with varied genres
            var artists = new List<Artist>();
            var genres = new[] { "Rock", "Alternative", "Jazz", "Electronic", "Metal" };
            for (int i = 1; i <= 40; i++)
            {
                artists.Add(CreateArtistWithGenres(i, $"Artist{i}",
                    new[] { genres[(i - 1) % genres.Length], genres[i % genres.Length] }));
            }

            var albums = new List<Album>();
            for (int i = 1; i <= 80; i++)
            {
                var artistId = ((i - 1) % 40) + 1;
                albums.Add(CreateAlbum(i, artistId, $"Album{i}",
                    genres: new[] { genres[(i - 1) % genres.Length], "Indie" }));
            }

            var styleCatalog = new StyleCatalogService(Logger, httpClient: null);

            var parallelBuilder = new StyleContextBuilder(
                styleCatalog,
                new LibraryAnalyzerOptions { EnableParallelStyleContext = true, ParallelizationThreshold = 1 },
                Logger);
            var sequentialBuilder = new StyleContextBuilder(
                styleCatalog,
                new LibraryAnalyzerOptions { EnableParallelStyleContext = false },
                Logger);

            // Act
            var parallelCtx = parallelBuilder.Build(artists, albums);
            var sequentialCtx = sequentialBuilder.Build(artists, albums);

            // Assert: HasStyles flag
            parallelCtx.HasStyles.Should().Be(sequentialCtx.HasStyles,
                "parallel and sequential should agree on HasStyles");

            // StyleCoverage key sets
            var parallelCovKeys = parallelCtx.StyleCoverage.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var sequentialCovKeys = sequentialCtx.StyleCoverage.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            parallelCovKeys.Should().BeEquivalentTo(sequentialCovKeys,
                "parallel and sequential should produce the same StyleCoverage keys");

            // StyleCoverage values
            foreach (var key in sequentialCovKeys)
            {
                parallelCtx.StyleCoverage[key].Should().Be(sequentialCtx.StyleCoverage[key],
                    $"StyleCoverage count for '{key}' should match");
            }

            // AllStyleSlugs
            parallelCtx.AllStyleSlugs.OrderBy(s => s).Should().BeEquivalentTo(
                sequentialCtx.AllStyleSlugs.OrderBy(s => s),
                "parallel and sequential should produce equivalent AllStyleSlugs");

            // StyleIndex key sets
            parallelCtx.StyleIndex.ArtistsByStyle.Keys.OrderBy(k => k).Should().BeEquivalentTo(
                sequentialCtx.StyleIndex.ArtistsByStyle.Keys.OrderBy(k => k),
                "parallel and sequential should produce equivalent ArtistsByStyle keys");

            parallelCtx.StyleIndex.AlbumsByStyle.Keys.OrderBy(k => k).Should().BeEquivalentTo(
                sequentialCtx.StyleIndex.AlbumsByStyle.Keys.OrderBy(k => k),
                "parallel and sequential should produce equivalent AlbumsByStyle keys");

            // StyleIndex values (sorted ID lists)
            foreach (var key in sequentialCtx.StyleIndex.ArtistsByStyle.Keys)
            {
                parallelCtx.StyleIndex.ArtistsByStyle[key].Should().BeEquivalentTo(
                    sequentialCtx.StyleIndex.ArtistsByStyle[key],
                    $"ArtistsByStyle IDs for '{key}' should match");
            }

            foreach (var key in sequentialCtx.StyleIndex.AlbumsByStyle.Keys)
            {
                parallelCtx.StyleIndex.AlbumsByStyle[key].Should().BeEquivalentTo(
                    sequentialCtx.StyleIndex.AlbumsByStyle[key],
                    $"AlbumsByStyle IDs for '{key}' should match");
            }
        }

        // ─── Edge Cases ──────────────────────────────────────────────────────

        [Fact]
        public void Profile_LargeLibrary_CompletesInReasonableTime()
        {
            // Arrange: 500 artists, 1500 albums
            var artists = new List<Artist>();
            var genres = new[] { "Rock", "Pop", "Jazz", "Electronic", "Metal", "Folk", "Blues" };
            for (int i = 1; i <= 500; i++)
            {
                artists.Add(CreateArtistWithGenres(i, $"Artist{i}",
                    new[] { genres[(i - 1) % genres.Length] }));
            }

            var albums = new List<Album>();
            for (int i = 1; i <= 1500; i++)
            {
                var artistId = ((i - 1) % 500) + 1;
                albums.Add(CreateAlbum(i, artistId, $"Album{i}",
                    genres: new[] { genres[(i - 1) % genres.Length] },
                    releaseDate: new DateTime(1970 + (i % 55), 1, 1)));
            }

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            var sw = Stopwatch.StartNew();
            var profile = analyzer.AnalyzeLibrary();
            sw.Stop();

            // Assert
            profile.Should().NotBeNull();
            profile.TotalArtists.Should().Be(500);
            profile.TotalAlbums.Should().Be(1500);
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
                "analyzing 500 artists and 1500 albums should complete in reasonable time");
        }

        [Fact]
        public void Profile_NullMetadata_HandlesGracefully()
        {
            // Arrange: artists with null Metadata
            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "NullMetaArtist", Monitored = true, Added = DateTime.UtcNow.AddMonths(-6) },
                new Artist { Id = 2, Name = "NullMetaArtist2", Monitored = true, Added = DateTime.UtcNow.AddMonths(-3) }
            };

            var albums = new List<Album>
            {
                CreateAlbum(1, 1, "SomeAlbum"),
                CreateAlbum(2, 2, "AnotherAlbum")
            };

            var analyzer = CreateAnalyzer(artists, albums);

            // Act
            Action act = () => analyzer.AnalyzeLibrary();

            // Assert
            act.Should().NotThrow("null metadata on artists should be handled gracefully");
            var profile = analyzer.AnalyzeLibrary();
            profile.Should().NotBeNull();
        }

        // ─── GetAllArtists / GetAllAlbums pass-through ──────────────────────

        [Fact]
        public void GetAllArtists_ReturnsServiceResult()
        {
            // Arrange
            var expected = new List<Artist>
            {
                CreateArtist(1, "PassthroughArtist")
            };

            var artistService = new Mock<IArtistService>();
            artistService.Setup(s => s.GetAllArtists()).Returns(expected);
            var albumService = new Mock<IAlbumService>();
            albumService.Setup(s => s.GetAllAlbums()).Returns(new List<Album>());
            var styleCatalog = new StyleCatalogService(Logger, httpClient: null);
            var analyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, Logger);

            // Act
            var result = analyzer.GetAllArtists();

            // Assert
            result.Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void GetAllAlbums_ReturnsServiceResult()
        {
            // Arrange
            var expected = new List<Album>
            {
                CreateAlbum(1, 1, "PassthroughAlbum")
            };

            var artistService = new Mock<IArtistService>();
            artistService.Setup(s => s.GetAllArtists()).Returns(new List<Artist>());
            var albumService = new Mock<IAlbumService>();
            albumService.Setup(s => s.GetAllAlbums()).Returns(expected);
            var styleCatalog = new StyleCatalogService(Logger, httpClient: null);
            var analyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, Logger);

            // Act
            var result = analyzer.GetAllAlbums();

            // Assert
            result.Should().BeEquivalentTo(expected);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private LibraryAnalyzer CreateAnalyzer(List<Artist> artists, List<Album> albums, LibraryAnalyzerOptions options = null)
        {
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            artistService.Setup(s => s.GetAllArtists()).Returns(artists);
            albumService.Setup(s => s.GetAllAlbums()).Returns(albums);

            var styleCatalog = new StyleCatalogService(Logger, httpClient: null);
            return new LibraryAnalyzer(artistService.Object, albumService.Object, styleCatalog, Logger, options);
        }

        private static Artist CreateArtistWithGenres(int id, string name, string[] genres)
        {
            var metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = name, Genres = genres.ToList() });
            return new Artist { Id = id, Name = name, Monitored = true, Added = DateTime.UtcNow.AddMonths(-12), Metadata = metadata };
        }

        private static Artist CreateArtist(int id, string name, bool monitored = true)
        {
            var metadata = new LazyLoaded<ArtistMetadata>(new ArtistMetadata { Name = name });
            return new Artist { Id = id, Name = name, Monitored = monitored, Added = DateTime.UtcNow.AddMonths(-12), Metadata = metadata };
        }

        private static Album CreateAlbum(int id, int artistId, string title, string[] genres = null, DateTime? releaseDate = null)
        {
            return new Album { Id = id, ArtistId = artistId, Title = title, Genres = genres?.ToList(), ReleaseDate = releaseDate, Monitored = true, AlbumType = "Album" };
        }

    }
}
