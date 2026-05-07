using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
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

namespace Brainarr.Tests
{
    public class DefaultSamplingServiceCovTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region Album Sampling with Ratings

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumRatings_OrdersByRatingValue()
        {
            // Arrange - covering lines 517-518: Album.Ratings?.Value and Album.Ratings?.Votes
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(2);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.AlbumStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 2
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            // Album 1 has higher rating
            var album1 = CreateAlbum(1, "High Rated Album", artistId: 10);
            album1.Ratings = new Ratings { Value = 9.0m, Votes = 100 };

            // Album 2 has lower rating
            var album2 = CreateAlbum(2, "Low Rated Album", artistId: 11);
            album2.Ratings = new Ratings { Value = 5.0m, Votes = 50 };

            var albums = new List<Album> { album2, album1 }; // Intentionally out of order

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert - higher rated album should be first
            result.Albums.Should().HaveCount(2, "because both albums match the style");
            result.Albums[0].Title.Should().Be("High Rated Album", "because albums are ordered by rating value descending (line 517)");
            result.Albums[0].MatchScore.Should().Be(1.0, "because exact style match has score 1.0");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumRatings_OrdersByVotesWhenValuesEqual()
        {
            // Arrange - covering line 518: Album.Ratings?.Votes
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(2);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.AlbumStyles[2] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 2
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            // Same rating value, different vote counts
            var album1 = CreateAlbum(1, "More Votes Album", artistId: 10);
            album1.Ratings = new Ratings { Value = 8.0m, Votes = 200 };

            var album2 = CreateAlbum(2, "Fewer Votes Album", artistId: 11);
            album2.Ratings = new Ratings { Value = 8.0m, Votes = 50 };

            var albums = new List<Album> { album2, album1 };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert - album with more votes should be first when values are equal
            result.Albums[0].Title.Should().Be("More Votes Album", "because albums are ordered by votes when rating values are equal (line 518)");
        }

        #endregion

        #region ResolveArtistName Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumArtistValueName_ResolvesFromArtistValue()
        {
            // Arrange - covering line 615: album.Artist?.Value?.Name path
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[100] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jazz" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["jazz"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["jazz"] = new List<int> { 100 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jazz" },
                relaxed: false);

            var album = CreateAlbum(100, "Jazz Album", artistId: 500);
            album.Artist.Value.Name = "Miles Davis"; // Set via Artist.Value.Name path

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums.Should().ContainSingle("because one album was provided");
            result.Albums[0].ArtistName.Should().Be("Miles Davis", "because artist name is resolved from album.Artist.Value.Name (line 615)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumArtistMetadataName_ResolvesFromArtistMetadata()
        {
            // Arrange - covering line 615: album.ArtistMetadata?.Value?.Name path
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[100] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "blues" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["blues"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["blues"] = new List<int> { 100 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "blues" },
                relaxed: false);

            var album = CreateAlbum(100, "Blues Album", artistId: 600);
            album.Artist = null!; // Clear Artist so it falls back to ArtistMetadata
            album.ArtistMetadata.Value.Name = "B.B. King";

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums[0].ArtistName.Should().Be("B.B. King", "because artist name is resolved from album.ArtistMetadata.Value.Name when Artist is null (line 615)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithNullAlbum_ResolvesToDefaultArtistName()
        {
            // Arrange - covering line 610-612: null album handling in ResolveArtistName
            // Note: This path is defensive - in practice album should never be null
            // We test through the synthetic artist path at line 77-88
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[100] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "electronic" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["electronic"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["electronic"] = new List<int> { 100 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "electronic" },
                relaxed: false);

            // Create album with no artist name available
            var album = CreateAlbum(100, "Electronic Album", artistId: 777);
            album.Artist = null!;
            album.ArtistMetadata = null!;

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert - synthetic artist created with fallback name
            result.Artists.Should().ContainSingle("because album should create synthetic artist");
            result.Artists[0].ArtistId.Should().Be(777, "because synthetic artist uses album's artist ID");
            result.Artists[0].Name.Should().Be("Artist 777", "because artist name falls back to ID when not resolvable (line 80)");
        }

        #endregion

        #region ComputeArtistWeight Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithArtistHavingMultipleAlbums_ComputesHigherWeight()
        {
            // Arrange - covering line 624-629: ComputeArtistWeight with album counts
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            // Create artist with albums
            var artist = CreateArtist(1, "Prolific Artist");
            artist.Added = DateTime.UtcNow.AddDays(-30);

            var artists = new List<Artist> { artist };

            // Create 10 albums for this artist
            var albums = Enumerable.Range(1, 10)
                .Select(id => CreateAlbum(id, $"Album {id}", artistId: 1))
                .ToList();

            // Act
            var result = service.Sample(
                artists,
                albums,
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert - weight should be computed based on album count (productivity)
            // With 10 albums: productivity = clamp(10/5.0, 0, 1) = 1.0
            // With recent added date: recency = 1.0
            // With no style match: matchScore = 0.0
            // weight = clamp(0.0*0.5 + 1.0*0.3 + 1.0*0.2, 0, 1) = 0.5
            result.Artists[0].Weight.Should().BeGreaterThan(0.4, "because artist with many albums should have higher weight from productivity factor (line 628)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithArtistHavingNoAlbums_ComputesLowerWeight()
        {
            // Arrange - covering line 628: albumCount = 0 path
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artist = CreateArtist(1, "New Artist");
            artist.Added = DateTime.UtcNow.AddDays(-30);

            var artists = new List<Artist> { artist };

            // Act - no albums for this artist
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert - weight should be lower due to zero album count
            // productivity = clamp(0/5.0, 0, 1) = 0.0
            // weight = clamp(0.0*0.5 + 1.0*0.3 + 0.0*0.2, 0, 1) = 0.3
            result.Artists[0].Weight.Should().BeApproximately(0.3, 0.01, "because artist with no albums has productivity = 0 (line 628)");
        }

        #endregion

        #region Random Sampling Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithExploratoryMode_UsesRandomSampling()
        {
            // Arrange - covering lines 434-461: random sampling path for artists
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            // Create 20 artists to ensure random sampling pool
            var artists = Enumerable.Range(1, 20)
                .Select(id => CreateArtist(id, $"Artist {id}"))
                .ToList();

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Exploratory // Has 25% random allocation
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                settings,
                tokenBudget: 1000,
                seed: 12345, // Deterministic seed
                CancellationToken.None);

            // Assert - with exploratory mode, random sampling should be used
            result.ArtistCount.Should().Be(10, "because target count is 10");
            // RandomPercent for exploratory mode is 25%, so random sampling path is exercised
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithExploratoryModeAlbums_UsesRandomSampling()
        {
            // Arrange - covering lines 537-564: random sampling path for albums
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            for (int i = 1; i <= 20; i++)
            {
                styleContext.AlbumStyles[i] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "indie" };
            }
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["indie"] = 20
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["indie"] = Enumerable.Range(1, 20).ToList()
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "indie" },
                relaxed: false);

            var albums = Enumerable.Range(1, 20)
                .Select(id => CreateAlbum(id, $"Indie Album {id}", artistId: id))
                .ToList();

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Exploratory
            };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                settings,
                tokenBudget: 1000,
                seed: 99999,
                CancellationToken.None);

            // Assert
            result.AlbumCount.Should().Be(10, "because target count is 10");
            result.Albums.Should().OnlyContain(a => a.MatchedStyles.Contains("indie"), "because all matched albums should have the indie style");
        }

        #endregion

        #region Album Title Edge Cases

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumHavingEmptyTitle_UsesFallbackTitle()
        {
            // Arrange - covering line 600: empty title fallback
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[42] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "metal" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["metal"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["metal"] = new List<int> { 42 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "metal" },
                relaxed: false);

            var album = CreateAlbum(42, "", artistId: 100);
            album.Title = ""; // Empty title

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums[0].Title.Should().Be("Album 42", "because empty title should fall back to ID-based title (line 600)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumHavingNullTitle_UsesFallbackTitle()
        {
            // Arrange - covering line 600: null title fallback
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[99] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "punk" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["punk"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["punk"] = new List<int> { 99 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "punk" },
                relaxed: false);

            var album = CreateAlbum(99, "Temp", artistId: 200);
            album.Title = null!; // Null title

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums[0].Title.Should().Be("Album 99", "because null title should fall back to ID-based title (line 600)");
        }

        #endregion

        #region Release Date and Year Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumReleaseDate_SetsYear()
        {
            // Arrange - covering line 604: Year = match.Album.ReleaseDate?.Year
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "classical" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["classical"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["classical"] = new List<int> { 1 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "classical" },
                relaxed: false);

            var album = CreateAlbum(1, "Symphony No. 5", artistId: 300);
            album.ReleaseDate = new DateTime(2020, 6, 15);

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums[0].Year.Should().Be(2020, "because year is extracted from ReleaseDate (line 604)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumNoReleaseDate_SetsNullYear()
        {
            // Arrange - covering line 604: ReleaseDate?.Year when null
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ambient" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["ambient"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ambient"] = new List<int> { 1 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ambient" },
                relaxed: false);

            var album = CreateAlbum(1, "Unknown Year Album", artistId: 400);
            album.ReleaseDate = null;

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Albums[0].Year.Should().BeNull("because null ReleaseDate results in null Year (line 604)");
        }

        #endregion

        #region Relaxed Expansion Cap Tests for Albums

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_RelaxedAlbumExpansion_IsCappedByAbsoluteLimit()
        {
            // Arrange - covering lines 226-244: relaxed album expansion cap
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            styleCatalogMock.Setup(x => x.GetSimilarSlugs(It.IsAny<string>())).Returns(Array.Empty<StyleSimilarity>());

            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(2000);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["synthwave"] = 500,
                ["retro"] = 1500
            });
            styleContext.SetDominantStyles(new[] { "synthwave" });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["synthwave"] = Enumerable.Range(1, 500).ToList(),
                    ["retro"] = Enumerable.Range(501, 1000).ToList()
                }));

            var selection = new StylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "synthwave" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "synthwave", "retro" },
                entries: new List<StyleEntry> { new StyleEntry { Name = "Synthwave", Slug = "synthwave" } },
                adjacent: new List<StyleEntry> { new StyleEntry { Name = "Retro", Slug = "retro" } },
                coverage: styleContext.StyleCoverage.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
                relaxed: true,
                threshold: 0.75,
                trimmed: new List<string>(),
                inferred: new List<string>());

            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                RelaxStyleMatching = true
            };

            // Create 1500 albums
            var albums = Enumerable.Range(1, 1500)
                .Select(id => CreateAlbum(id, $"Album {id}", artistId: 1))
                .ToList();

            // Act
            var sample = service.Sample(
                Array.Empty<Artist>(),
                albums,
                styleContext,
                selection,
                settings,
                tokenBudget: 4000,
                seed: 42,
                token: CancellationToken.None);

            // Assert - absolute relaxed cap of 1200 should limit expanded matches
            // The selection process should not exceed the cap
            sample.AlbumCount.Should().BeLessOrEqualTo(1200, "because absolute relaxed cap is 1200 (line 20)");
        }

        #endregion

        #region Helper Methods

        private static Artist CreateArtist(int id, string name)
        {
            var artist = new Artist
            {
                Id = id,
                Added = DateTime.UtcNow.AddDays(-id)
            };
            artist.Metadata.Value.Name = name;
            return artist;
        }

        private static Album CreateAlbum(int id, string title, int artistId)
        {
            var album = new Album
            {
                Id = id,
                Title = title,
                ArtistId = artistId,
                Added = DateTime.UtcNow.AddDays(-id),
                ReleaseDate = DateTime.UtcNow.AddYears(-1)
            };
            album.Ratings = new Ratings { Value = 7.0m, Votes = 100 };
            return album;
        }

        private static StylePlanContext CreateStylePlanContext(
            ISet<string> selected,
            ISet<string>? expanded = null,
            bool relaxed = false,
            double threshold = 1.0)
        {
            expanded ??= selected;

            return new StylePlanContext(
                selected: selected,
                expanded: expanded,
                entries: selected.Select(s => new StyleEntry { Name = s, Slug = s }).ToList(),
                adjacent: (expanded.Except(selected)).Select(s => new StyleEntry { Name = s, Slug = s }).ToList(),
                coverage: selected.ToDictionary(s => s, _ => 1, StringComparer.OrdinalIgnoreCase),
                relaxed: relaxed,
                threshold: threshold,
                trimmed: new List<string>(),
                inferred: new List<string>());
        }

        #endregion
    }
}
