using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using Xunit;
using Lidarr.Plugin.Common.Abstractions.Llm;

namespace Brainarr.Tests.BugFixes
{
    /// <summary>
    /// Test for the bug fix where artists were getting duplicated up to 8 times in Lidarr.
    /// The bug was caused by ConvertToImportListItems not deduplicating recommendations.
    /// </summary>
    [Trait("Category", "BugFix")]
    public class DuplicateRecommendationFixTests
    {
        private readonly Logger _logger;

        public DuplicateRecommendationFixTests()
        {
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact]
        public void DeduplicateRecommendations_WithDuplicates_RemovesDuplicates()
        {
            // Arrange - Create service directly
            var duplicationService = new DuplicationPreventionService(_logger);

            // Create recommendations with exact duplicates
            var duplicatedItems = new List<ImportListItemInfo>
            {
                // Same artist/album appears multiple times
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall", ReleaseDate = new DateTime(1979, 1, 1) },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall", ReleaseDate = new DateTime(1979, 1, 1) },
                new ImportListItemInfo { Artist = "Pink Floyd", Album = "The Wall", ReleaseDate = new DateTime(1979, 1, 1) },

                // Another duplicate pair
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV", ReleaseDate = new DateTime(1971, 1, 1) },
                new ImportListItemInfo { Artist = "Led Zeppelin", Album = "IV", ReleaseDate = new DateTime(1971, 1, 1) },

                // Unique recommendation
                new ImportListItemInfo { Artist = "The Beatles", Album = "Abbey Road", ReleaseDate = new DateTime(1969, 1, 1) }
            };

            // Act - Test the deduplication directly
            var result = duplicationService.DeduplicateRecommendations(duplicatedItems);

            // Assert - Should have removed exact duplicates
            Assert.NotNull(result);
            Assert.Equal(3, result.Count); // Only 3 unique artist/album combinations

            // Verify each unique artist appears only once
            var artistAlbums = result.Select(r => $"{r.Artist}|{r.Album}").ToList();
            Assert.Equal(3, artistAlbums.Distinct().Count());

            // Verify the correct artists are present
            var artists = result.Select(r => r.Artist).ToList();
            Assert.Contains("Pink Floyd", artists);
            Assert.Contains("Led Zeppelin", artists);
            Assert.Contains("The Beatles", artists);
        }

        [Fact]
        public async Task FetchRecommendationsAsync_IntegrationTest_DeduplicatesInWorkflow()
        {
            var httpClientMock = new Mock<IHttpClient>();
            var artistServiceMock = new Mock<IArtistService>();
            var albumServiceMock = new Mock<IAlbumService>();

            var mockArtists = new List<Artist>
            {
                new Artist { Id = 1, Name = "Test Artist" }
            };
            var mockAlbums = new List<Album>
            {
                new Album { Id = 1, Title = "Test Album", ArtistId = 1 }
            };

            artistServiceMock.Setup(x => x.GetAllArtists()).Returns(mockArtists);
            albumServiceMock.Setup(x => x.GetAllAlbums()).Returns(mockAlbums);

            var styleCatalog = new StyleCatalogService(_logger, httpClient: null);
            var libraryAnalyzer = new LibraryAnalyzer(artistServiceMock.Object, albumServiceMock.Object, styleCatalog, _logger);
            var originalConfig = LogManager.Configuration;

            var providerFactoryMock = new Mock<IProviderFactory>();
            var cache = new RecommendationCache(_logger);
            var healthMonitorMock = new Mock<IProviderHealthMonitor>();
            var validatorMock = new Mock<IRecommendationValidator>();
            var modelDetectionMock = new Mock<IModelDetectionService>();
            using var duplicationPrevention = new DuplicationPreventionService(_logger);
            duplicationPrevention.ClearHistory();

            var tempAppData = Path.Combine(Path.GetTempPath(), "BrainarrTests", "AppData", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempAppData);
            var originalAppData = Environment.GetEnvironmentVariable("APPDATA");

            try
            {
                Environment.SetEnvironmentVariable("APPDATA", tempAppData);

                var duplicatedRecommendations = new List<Recommendation>
                {
                    new Recommendation { Artist = "Artist 1", Album = "Album 1", Year = 2020, Confidence = 0.9 },
                    new Recommendation { Artist = "Artist 1", Album = "Album 1", Year = 2020, Confidence = 0.8 },
                    new Recommendation { Artist = "Artist 2", Album = "Album 2", Year = 2021, Confidence = 0.9 }
                };

                var mockProvider = new Mock<IAIProvider>();
                mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
                mockProvider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(ProviderHealthResult.Healthy(responseTime: TimeSpan.FromSeconds(1)));
                mockProvider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                           .ReturnsAsync(duplicatedRecommendations);

                providerFactoryMock.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                                  .Returns(mockProvider.Object);

                healthMonitorMock.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);

                validatorMock.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                            .Returns((List<Recommendation> recs, bool strict) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                            {
                                ValidRecommendations = recs ?? new List<Recommendation>(),
                                FilteredRecommendations = new List<Recommendation>(),
                                TotalCount = recs?.Count ?? 0,
                                ValidCount = recs?.Count ?? 0,
                                FilteredCount = 0
                            });

                var settings = new BrainarrSettings
                {
                    Provider = AIProvider.OpenAI,
                    OpenAIApiKey = "test-key",
                    MaxRecommendations = 10
                };

                var orchestrator = new BrainarrOrchestrator(
                    _logger,
                    providerFactoryMock.Object,
                    libraryAnalyzer,
                    cache,
                    healthMonitorMock.Object,
                    validatorMock.Object,
                    modelDetectionMock.Object,
                    httpClientMock.Object,
                    duplicationPrevention,
                    breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object);

                orchestrator.InitializeProvider(settings);
                var result = await orchestrator.FetchRecommendationsAsync(settings);

                Assert.NotNull(result);
                Assert.Equal(2, result.Count);
                var artists = result.Select(r => r.Artist).Distinct().ToList();
                Assert.Equal(2, artists.Count);
            }
            finally
            {
                duplicationPrevention.ClearHistory();
                Environment.SetEnvironmentVariable("APPDATA", originalAppData);
                try
                {
                    if (Directory.Exists(tempAppData))
                    {
                        Directory.Delete(tempAppData, true);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
