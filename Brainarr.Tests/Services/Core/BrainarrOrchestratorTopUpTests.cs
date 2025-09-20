using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NLog;
using Xunit;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    [Trait("Component", "Orchestrator")]
    public class BrainarrOrchestratorTopUpTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        public async Task FetchRecommendations_WithTopUpEnabled_FillsToTarget()
        {
            // Arrange
            var http = new Mock<IHttpClient>();
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            var providerFactory = new Mock<IProviderFactory>();
            var cache = new Mock<IRecommendationCache>();
            var health = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();

            // Existing library has Arctic Monkeys - The Car (to force a duplicate removal)
            artistService.Setup(a => a.GetAllArtists()).Returns(new List<Artist>
            {
                new Artist { Id = 1, Name = "Arctic Monkeys" }
            });
            albumService.Setup(a => a.GetAllAlbums()).Returns(new List<Album>
            {
                new Album { Id = 1, Title = "The Car", ArtistId = 1 }
            });

            // Real LibraryAnalyzer for duplicate filtering against mocks
            var styleCatalog = new Mock<IStyleCatalogService>();
            var libraryAnalyzer = new LibraryAnalyzer(artistService.Object, albumService.Object, _logger, styleCatalog.Object);

            // Cache miss
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out It.Ref<List<ImportListItemInfo>>.IsAny))
                 .Returns(false);

            // Health OK
            health.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);

            // Validator passes through
            validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                     .Returns((List<Recommendation> recs, bool strict) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                     {
                         ValidRecommendations = recs ?? new List<Recommendation>(),
                         FilteredRecommendations = new List<Recommendation>(),
                         TotalCount = recs?.Count ?? 0,
                         ValidCount = recs?.Count ?? 0,
                         FilteredCount = 0
                     });

            // Provider that returns 2 items first (one dup), then 1 unique item for top-up
            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("LM Studio");
            provider.SetupSequence(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                    .ReturnsAsync(new List<Recommendation>
                    {
                        new Recommendation { Artist = "Arctic Monkeys", Album = "The Car", Confidence = 0.9 },
                        new Recommendation { Artist = "Lana Del Rey", Album = "Ocean Blvd", Confidence = 0.9 }
                    })
                    .ReturnsAsync(new List<Recommendation>
                    {
                        new Recommendation { Artist = "Phoebe Bridgers", Album = "Punisher", Confidence = 0.9 }
                    });
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);

            providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), http.Object, _logger))
                           .Returns(provider.Object);

            var orchestrator = new BrainarrOrchestrator(
                _logger,
                providerFactory.Object,
                libraryAnalyzer,
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                null,
                styleCatalog: styleCatalog.Object);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.LMStudio,
                LMStudioUrl = "http://localhost:1234",
                LMStudioModel = "test-model",
                MaxRecommendations = 2,
                EnableIterativeRefinement = true,
                RequireMbids = false // keep enrichment permissive for unit test
            };

            // Act
            var result = await orchestrator.FetchRecommendationsAsync(settings);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Contains(result, r => r.Artist == "Lana Del Rey");
            Assert.Contains(result, r => r.Artist == "Phoebe Bridgers");
        }
    }
}
