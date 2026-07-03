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

namespace Brainarr.Tests.Services.Prompting
{
    public class DefaultSamplingServiceCovTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region Constructor Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var styleCatalog = new Mock<IStyleCatalogService>().Object;
            var contextPolicy = new Mock<IContextPolicy>().Object;

            // Act
            var act = () => new DefaultSamplingService(null!, styleCatalog, contextPolicy);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("logger");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Constructor_WithNullStyleCatalog_ThrowsArgumentNullException()
        {
            // Arrange
            var contextPolicy = new Mock<IContextPolicy>().Object;

            // Act
            var act = () => new DefaultSamplingService(Logger, null!, contextPolicy);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("styleCatalog");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Constructor_WithNullContextPolicy_ThrowsArgumentNullException()
        {
            // Arrange
            var styleCatalog = new Mock<IStyleCatalogService>().Object;

            // Act
            var act = () => new DefaultSamplingService(Logger, styleCatalog, null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("contextPolicy");
        }

        #endregion

        #region Cancellation Token Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            var styleCatalog = new Mock<IStyleCatalogService>().Object;
            var contextPolicy = new Mock<IContextPolicy>().Object;
            var service = new DefaultSamplingService(Logger, styleCatalog, contextPolicy);
            var cancelledToken = new CancellationToken(true);

            // Act
            var act = () => service.Sample(
                Array.Empty<Artist>(),
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                token: cancelledToken);

            // Assert
            act.Should().Throw<OperationCanceledException>();
        }

        #endregion

        #region Null Parameter Handling Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithNullStyleContext_CreatesDefaultContext()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Test Artist")
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext: null!,
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.ArtistCount.Should().Be(1);
            result.Artists[0].Name.Should().Be("Test Artist");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithNullSettings_CreatesDefaultSettings()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Test Artist")
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                settings: null!,
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.ArtistCount.Should().Be(1);
        }

        #endregion

        #region Sparse Flag Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithFewArtistMatchesAndStyles_SetsSparseFlag()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            styleCatalogMock.Setup(x => x.GetSimilarSlugs(It.IsAny<string>())).Returns(Array.Empty<StyleSimilarity>());

            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 3
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2, 3 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var artists = Enumerable.Range(1, 3)
                .Select(id => CreateArtist(id, $"Artist {id}"))
                .ToList();

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            selection.Sparse.Should().BeTrue("because artist matches < 5 with styles selected");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithFewAlbumMatchesAndStyles_SetsSparseFlag()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            styleCatalogMock.Setup(x => x.GetSimilarSlugs(It.IsAny<string>())).Returns(Array.Empty<StyleSimilarity>());

            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 3
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1, 2, 3 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var albums = Enumerable.Range(1, 3)
                .Select(id => CreateAlbum(id, $"Album {id}", artistId: 1))
                .ToList();

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
            selection.Sparse.Should().BeTrue("because album matches < 5 with styles selected");
        }

        #endregion

        #region Synthetic Artist Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumHavingMissingArtist_CreatesSyntheticArtist()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[100] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 100 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var album = CreateAlbum(100, "Test Album", artistId: 999);
            album.ArtistMetadataId = 999;
            // Artist exists in the library but is not sampled (target artist count = 0), so the album
            // still brings along a synthetic artist. Its id/name resolve from the artists list via
            // ArtistMetadataId (a nameless artist -> "Artist {id}" fallback), not an album lazy-load.
            var artist = new Artist { Id = 999, ArtistMetadataId = 999, Name = null };

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                new List<Artist> { artist },
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Artists.Should().Contain(a => a.ArtistId == 999, "because synthetic artist should be created for album's artist");
            var syntheticArtist = result.Artists.First(a => a.ArtistId == 999);
            syntheticArtist.Name.Should().Be("Artist 999", "because artist name falls back to ID when not resolvable");
            syntheticArtist.Weight.Should().Be(0.25);
            syntheticArtist.Albums.Should().ContainSingle();
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithAlbumHavingEmptyArtistName_UsesFallbackName()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.AlbumStyles[100] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 100 }
                }));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var album = CreateAlbum(100, "Test Album", artistId: 888);
            album.ArtistMetadataId = 888;
            // Nameless artist in the library -> "Artist {id}" fallback, resolved via ArtistMetadataId.
            var artist = new Artist { Id = 888, ArtistMetadataId = 888, Name = null };

            var albums = new List<Album> { album };

            // Act
            var result = service.Sample(
                new List<Artist> { artist },
                albums,
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            var syntheticArtist = result.Artists.First(a => a.ArtistId == 888);
            syntheticArtist.Name.Should().Be("Artist 888", "because empty artist name should fall back to ID-based name");
        }

        #endregion

        #region Empty/Zero Target Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithZeroTargetArtistCount_ReturnsEmptyArtists()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Test Artist")
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(0);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithEmptyArtistsAndAlbums_ReturnsEmptySample()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            // Act
            var result = service.Sample(
                Array.Empty<Artist>(),
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(0);
            result.AlbumCount.Should().Be(0);
        }

        #endregion

        #region Style Matching Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithNoSelectedStyles_IncludesAllArtists()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artists = Enumerable.Range(1, 3)
                .Select(id => CreateArtist(id, $"Artist {id}"))
                .ToList();

            // Act - StylePlanContext.Empty has no selected styles
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(3, "because with no styles selected, all artists should be included");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithRelaxedMatching_MatchesViaSimilarSlugs()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            styleCatalogMock
                .Setup(x => x.GetSimilarSlugs("indie-rock"))
                .Returns(new List<StyleSimilarity>
                {
                    new StyleSimilarity("rock", 0.85, "parent")
                });

            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "indie-rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1,
                ["indie-rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock", "indie-rock" },
                relaxed: true,
                threshold: 0.75);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Indie Rock Artist")
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(1, "because relaxed matching should match via similar slug");
            result.Artists[0].MatchedStyles.Should().Contain("rock");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithRelaxedMatchingBelowThreshold_ExcludesMatch()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            styleCatalogMock
                .Setup(x => x.GetSimilarSlugs("indie-rock"))
                .Returns(new List<StyleSimilarity>
                {
                    new StyleSimilarity("rock", 0.5, "parent") // Below threshold of 0.75
                });

            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "indie-rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1,
                ["indie-rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int>()
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                expanded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock", "indie-rock" },
                relaxed: true,
                threshold: 0.75);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Indie Rock Artist")
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(0, "because match score below threshold should be excluded");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithArtistHavingNoStylesAndStylesSelected_ExcludesArtist()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(10);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var artists = new List<Artist>
            {
                CreateArtist(1, "No Style Artist")
                // Note: No ArtistStyles entry for this artist means no styles
            };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.ArtistCount.Should().Be(0, "because artist with no styles should be excluded when styles are selected");
        }

        #endregion

        #region Artist Name Resolution Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithArtistHavingNullName_UsesFallbackName()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artist = new Artist
            {
                Id = 42,
                Added = DateTime.UtcNow
            };
            artist.Metadata.Value.Name = null!;

            var artists = new List<Artist> { artist };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Artists[0].Name.Should().Be("Artist 42", "because null artist name should fall back to ID-based name");
        }

        #endregion

        #region NormalizeAdded Tests (via Sample)

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithDefaultAddedDate_HandlesGracefully()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var artist = CreateArtist(1, "Test Artist");
            artist.Added = default; // DateTime default

            var artists = new List<Artist> { artist };

            // Act
            var result = service.Sample(
                artists,
                Array.Empty<Album>(),
                new LibraryStyleContext(),
                StylePlanContext.Empty,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            result.Artists[0].Added.Should().Be(DateTime.MinValue);
        }

        #endregion

        #region Match Registration Tests

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Sampling")]
        public void Sample_WithStyles_RegistersMatchesWithSelection()
        {
            // Arrange
            var styleCatalogMock = new Mock<IStyleCatalogService>();
            var contextPolicyMock = new Mock<IContextPolicy>();
            contextPolicyMock.Setup(x => x.DetermineTargetArtistCount(It.IsAny<int>(), It.IsAny<int>())).Returns(5);
            contextPolicyMock.Setup(x => x.DetermineTargetAlbumCount(It.IsAny<int>(), It.IsAny<int>())).Returns(0);

            var service = new DefaultSamplingService(Logger, styleCatalogMock.Object, contextPolicyMock.Object);

            var styleContext = new LibraryStyleContext();
            styleContext.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" };
            styleContext.SetCoverage(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["rock"] = 1
            });
            styleContext.SetStyleIndex(new LibraryStyleIndex(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["rock"] = new List<int> { 1 }
                },
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            var selection = CreateStylePlanContext(
                selected: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rock" },
                relaxed: false);

            var artists = new List<Artist>
            {
                CreateArtist(1, "Rock Artist")
            };

            // Act
            service.Sample(
                artists,
                Array.Empty<Album>(),
                styleContext,
                selection,
                new BrainarrSettings(),
                tokenBudget: 1000,
                seed: 42,
                CancellationToken.None);

            // Assert
            selection.MatchedCounts.Should().ContainKey("rock");
            selection.MatchedCounts["rock"].Should().BeGreaterThan(0);
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
            return new Album
            {
                Id = id,
                Title = title,
                ArtistId = artistId,
                Added = DateTime.UtcNow.AddDays(-id),
                ReleaseDate = DateTime.UtcNow.AddYears(-1)
            };
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
