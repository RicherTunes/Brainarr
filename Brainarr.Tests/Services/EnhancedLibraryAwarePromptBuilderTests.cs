using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class EnhancedLibraryAwarePromptBuilderTests
    {
        private readonly Logger _logger;
        private readonly LibraryAwarePromptBuilder _promptBuilder;

        public EnhancedLibraryAwarePromptBuilderTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _promptBuilder = new LibraryAwarePromptBuilder(_logger);
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseSimilarModeTemplate()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("Recommend exactly 10 new albums.");
            prompt.Should().Contain("Focus: Similar artists and albums deeply rooted in the collection's existing styles.");
            prompt.Should().Contain("Respect the listener's library even without explicit style anchors.");
            prompt.Should().Contain("RECOMMENDATION REQUIREMENTS:");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseAdjacentModeTemplate()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Adjacent,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("Focus: Adjacent discoveries with concrete ties to the collection (collaborators, labelmates, side projects).");
            prompt.Should().Contain("Respect the listener's library even without explicit style anchors.");
            prompt.Should().Contain("RECOMMENDATION REQUIREMENTS:");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseExploratoryModeTemplate()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                DiscoveryMode = DiscoveryMode.Exploratory,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("Focus: Exploratory finds that expand the listener's horizons while staying grounded in real connections.");
            prompt.Should().Contain("Respect the listener's library even without explicit style anchors.");
            prompt.Should().Contain("RECOMMENDATION REQUIREMENTS:");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseMinimalSamplingPreamble()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                SamplingStrategy = SamplingStrategy.Minimal,
                DiscoveryMode = DiscoveryMode.Similar,
                MaxRecommendations = 5,
                Provider = AIProvider.Ollama
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("[STYLE_AWARE] Use quick-hit sampling (minimal context).");
            prompt.Should().Contain("Note: Items below are representative samples of a much larger library; avoid recommending duplicates even if not explicitly listed.");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseComprehensiveSamplingPreamble()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                SamplingStrategy = SamplingStrategy.Comprehensive,
                DiscoveryMode = DiscoveryMode.Similar,
                MaxRecommendations = 15,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("[STYLE_AWARE] Use comprehensive sampling. Prioritize depth and adjacency clusters.");
            prompt.Should().Contain("Note: Items below are representative samples of a much larger library; avoid recommending duplicates even if not explicitly listed.");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldUseBalancedSamplingPreamble()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                SamplingStrategy = SamplingStrategy.Balanced,
                DiscoveryMode = DiscoveryMode.Adjacent,
                MaxRecommendations = 10,
                Provider = AIProvider.Anthropic
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("[STYLE_AWARE] Use balanced sampling with key artists/albums.");
            prompt.Should().Contain("Note: Items below are representative samples of a much larger library; avoid recommending duplicates even if not explicitly listed.");
            prompt.Should().Contain("RECOMMENDATION REQUIREMENTS:");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldIncludeCollectionStyleInContext()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            profile.Metadata["CollectionStyle"] = "Completionist - Collects full discographies";
            profile.Metadata["CompletionistScore"] = 75.5;
            profile.Metadata["AverageAlbumsPerArtist"] = 8.2;

            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                SamplingStrategy = SamplingStrategy.Comprehensive,
                DiscoveryMode = DiscoveryMode.Similar,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            prompt.Should().Contain("Collection style: Completionist - Collects full discographies (completionist score: 75.5%)");
            prompt.Should().Contain("avg 8.2 albums per artist");
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldIncludeGenreDistributionWithSignificance()
        {
            // Arrange
            var profile = CreateTestLibraryProfile();
            profile.Metadata["GenreDistribution"] = new Dictionary<string, double>
            {
                {"Rock", 45.0},
                {"Rock_significance", 3.0},
                {"Electronic", 25.0},
                {"Electronic_significance", 2.0},
                {"Jazz", 20.0},
                {"Jazz_significance", 2.0},
                {"Folk", 10.0},
                {"Folk_significance", 1.0}
            };

            var artists = CreateTestArtists();
            var albums = CreateTestAlbums();
            var settings = new BrainarrSettings
            {
                SamplingStrategy = SamplingStrategy.Comprehensive,
                DiscoveryMode = DiscoveryMode.Similar,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var prompt = _promptBuilder.BuildLibraryAwarePrompt(profile, artists, albums, settings);

            // Assert
            // Check that main genres are included in the prompt with their percentages
            prompt.Should().Contain("Rock (45.0%)")
                  .And.Contain("Electronic (25.0%)")
                  .And.Contain("Jazz (20.0%)");
        }
        [Fact]
        public void BuildLibraryAwarePrompt_ShouldLimitRelaxedStyleInflation()
        {
            // Arrange
            var styleCatalog = CreateRelaxedStyleCatalogMock();
            var builder = new LibraryAwarePromptBuilder(_logger, styleCatalog.Object, new ModelRegistryLoader(), new ModelTokenizerRegistry());

            var adjacentIds = new[] { 2, 3, 4 };
            var profile = CreateRelaxedProfile(adjacentIds);
            var artists = CreateRelaxedArtists(adjacentIds);
            var settings = new BrainarrSettings
            {
                RelaxStyleMatching = true,
                StyleFilters = new[] { "Primary" },
                MaxSelectedStyles = 5,
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var result = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, new List<Album>(), settings);

            // Assert
            result.MatchedStyleCounts.Should().ContainKey("primary");
            result.MatchedStyleCounts["primary"].Should().BeGreaterThan(0);
        }

        [Fact]
        public void BuildLibraryAwarePrompt_ShouldIncludeRelaxedMatchesWithinLimit()
        {
            // Arrange
            var styleCatalog = CreateRelaxedStyleCatalogMock();
            var builder = new LibraryAwarePromptBuilder(_logger, styleCatalog.Object, new ModelRegistryLoader(), new ModelTokenizerRegistry());

            var adjacentIds = new[] { 2 };
            var profile = CreateRelaxedProfile(adjacentIds);
            var artists = CreateRelaxedArtists(adjacentIds);
            var settings = new BrainarrSettings
            {
                RelaxStyleMatching = true,
                StyleFilters = new[] { "Primary" },
                MaxSelectedStyles = 5,
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                Provider = AIProvider.OpenAI
            };

            // Act
            var result = builder.BuildLibraryAwarePromptWithMetrics(profile, artists, new List<Album>(), settings);
            // Assert
            result.MatchedStyleCounts.Should().ContainKey("primary");
            result.MatchedStyleCounts["primary"].Should().BeGreaterThan(0);
        }

        private Mock<IStyleCatalogService> CreateRelaxedStyleCatalogMock()
        {
            var catalog = new Mock<IStyleCatalogService>();

            catalog.Setup(x => x.Normalize(It.IsAny<IEnumerable<string>>()))
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

                        set.Add(value.Trim().ToLowerInvariant());
                    }

                    return set;
                });

            catalog.Setup(x => x.GetBySlug(It.IsAny<string>()))
                .Returns<string>(slug => new StyleEntry { Slug = slug, Name = slug });

            catalog.Setup(x => x.GetSimilarSlugs(It.Is<string>(s => s.Equals("primary", StringComparison.OrdinalIgnoreCase))))
                .Returns(new[] { new StyleSimilarity { Slug = "adjacent", Score = 0.85 } });

            catalog.Setup(x => x.GetSimilarSlugs(It.Is<string>(s => s.Equals("adjacent", StringComparison.OrdinalIgnoreCase))))
                .Returns(new[] { new StyleSimilarity { Slug = "primary", Score = 0.85 } });

            catalog.Setup(x => x.GetSimilarSlugs(It.Is<string>(s => !s.Equals("primary", StringComparison.OrdinalIgnoreCase) && !s.Equals("adjacent", StringComparison.OrdinalIgnoreCase))))
                .Returns(Array.Empty<StyleSimilarity>());

            catalog.Setup(x => x.GetAll()).Returns(Array.Empty<StyleEntry>());

            return catalog;
        }

        private LibraryProfile CreateRelaxedProfile(IEnumerable<int> adjacentArtistIds)
        {
            var adjacentList = adjacentArtistIds?.ToList() ?? new List<int>();
            var context = BuildRelaxedStyleContext(adjacentList);

            return new LibraryProfile
            {
                TotalArtists = 1 + adjacentList.Count,
                TotalAlbums = 0,
                TopGenres = new Dictionary<string, int>(),
                TopArtists = new List<string>(),
                RecentlyAdded = new List<string>(),
                Metadata = new Dictionary<string, object>(),
                StyleContext = context
            };
        }

        private LibraryStyleContext BuildRelaxedStyleContext(IReadOnlyCollection<int> adjacentArtistIds)
        {
            var context = new LibraryStyleContext();
            context.ArtistStyles[1] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "primary" };

            var coverage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = 1
            };

            var expanded = new List<int> { 1 };

            foreach (var id in adjacentArtistIds)
            {
                context.ArtistStyles[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "adjacent" };
                coverage["adjacent"] = coverage.TryGetValue("adjacent", out var count) ? count + 1 : 1;
                expanded.Add(id);
            }

            var dominant = coverage
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => kvp.Key);

            context.SetCoverage(coverage);
            context.SetDominantStyles(dominant);

            var expandedOrdered = expanded.Distinct().OrderBy(id => id).ToList();

            var artistIndex = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
            {
                ["primary"] = new List<int> { 1 },
                ["adjacent"] = expandedOrdered.ToArray()
            };

            context.SetStyleIndex(new LibraryStyleIndex(
                artistIndex,
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)));

            return context;
        }

        private List<Artist> CreateRelaxedArtists(IEnumerable<int> adjacentArtistIds)
        {
            var list = new List<Artist>
            {
                new Artist
                {
                    Id = 1,
                    Name = "Primary Artist",
                    Monitored = true,
                    Added = DateTime.UtcNow.AddDays(-30),
                    Metadata = new ArtistMetadata { Genres = new List<string> { "Primary" } }
                }
            };

            foreach (var id in adjacentArtistIds)
            {
                list.Add(new Artist
                {
                    Id = id,
                    Name = $"Adjacent Artist {id}",
                    Monitored = true,
                    Added = DateTime.UtcNow.AddDays(-15),
                    Metadata = new ArtistMetadata { Genres = new List<string> { "Adjacent" } }
                });
            }

            return list;
        }

        // Helper methods
        private LibraryProfile CreateTestLibraryProfile()
        {
            return new LibraryProfile
            {
                TotalArtists = 50,
                TotalAlbums = 200,
                TopGenres = new Dictionary<string, int>
                {
                    {"Rock", 25},
                    {"Electronic", 15},
                    {"Jazz", 10}
                },
                TopArtists = new List<string> { "Artist 1", "Artist 2", "Artist 3" },
                RecentlyAdded = new List<string> { "Recent Artist 1", "Recent Artist 2" },
                Metadata = new Dictionary<string, object>
                {
                    {"CollectionSize", "substantial"},
                    {"CollectionFocus", "rock-electronic"},
                    {"AverageAlbumsPerArtist", 4.0}
                }
            };
        }

        private List<Artist> CreateTestArtists()
        {
            return new List<Artist>
            {
                new Artist { Id = 1, Name = "Test Artist 1", Monitored = true },
                new Artist { Id = 2, Name = "Test Artist 2", Monitored = true },
                new Artist { Id = 3, Name = "Test Artist 3", Monitored = false }
            };
        }

        private List<Album> CreateTestAlbums()
        {
            return new List<Album>
            {
                new Album { Id = 1, ArtistId = 1, Title = "Album 1", AlbumType = "Studio", Monitored = true },
                new Album { Id = 2, ArtistId = 1, Title = "Album 2", AlbumType = "Studio", Monitored = true },
                new Album { Id = 3, ArtistId = 2, Title = "Album 3", AlbumType = "Live", Monitored = false }
            };
        }
    }
}
