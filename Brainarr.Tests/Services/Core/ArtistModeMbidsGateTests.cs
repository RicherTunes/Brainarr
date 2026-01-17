using System;
using System.Collections.Generic;
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
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    [Trait("Component", "Orchestrator")]
    public class ArtistModeMbidsGateTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        public async Task ArtistMode_WithRequireMbids_OnlyArtistMbidsRequired()
        {
            var http = new Mock<IHttpClient>();
            var artistService = new Mock<IArtistService>();
            var albumService = new Mock<IAlbumService>();
            var providerFactory = new Mock<IProviderFactory>();
            var cache = new Mock<IRecommendationCache>();
            var health = new Mock<IProviderHealthMonitor>();
            var validator = new Mock<IRecommendationValidator>();
            var modelDetection = new Mock<IModelDetectionService>();
            var artistResolver = new Mock<IArtistMbidResolver>();

            // Empty library to avoid library duplicate filtering
            artistService.Setup(a => a.GetAllArtists()).Returns(new List<Artist>());
            albumService.Setup(a => a.GetAllAlbums()).Returns(new List<Album>());

            // Cache miss
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out It.Ref<List<ImportListItemInfo>>.IsAny))
                 .Returns(false);

            health.Setup(h => h.IsHealthy(It.IsAny<string>())).Returns(true);

            // Provider returns two artist-only recs
            var provider = new Mock<IAIProvider>();
            provider.SetupGet(p => p.ProviderName).Returns("OpenAI");
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>()))
                    .ReturnsAsync(new List<Recommendation>
                    {
                        new Recommendation { Artist = "ArtistWithMBID", Album = "", Confidence = 0.9 },
                        new Recommendation { Artist = "ArtistNoMBID", Album = "", Confidence = 0.9 },
                    });
            provider.Setup(p => p.TestConnectionAsync()).ReturnsAsync(true);
            providerFactory.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), http.Object, _logger))
                           .Returns(provider.Object);

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

            // Artist resolver enriches only one with MBID
            artistResolver.Setup(r => r.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), default))
                          .ReturnsAsync((List<Recommendation> recs, System.Threading.CancellationToken _) =>
                          {
                              var enriched = new List<Recommendation>();
                              foreach (var r in recs)
                              {
                                  if (r.Artist == "ArtistWithMBID")
                                  {
                                      enriched.Add(r with { ArtistMusicBrainzId = "mbid-123" });
                                  }
                                  else
                                  {
                                      enriched.Add(r with { ArtistMusicBrainzId = null });
                                  }
                              }
                              return enriched;
                          });

            var breakerRegistry = new Mock<IBreakerRegistry>();
            breakerRegistry
                .Setup(r => r.Get(It.IsAny<ModelKey>(), It.IsAny<Logger>(), It.IsAny<CircuitBreakerOptions?>()))
                .Returns(new PassThroughCircuitBreaker());

            var orchestrator = new BrainarrOrchestrator(
                _logger,
                providerFactory.Object,
                new LibraryAnalyzer(artistService.Object, albumService.Object, new StyleCatalogService(_logger, httpClient: null), _logger),
                cache.Object,
                health.Object,
                validator.Object,
                modelDetection.Object,
                http.Object,
                null,
                null,
                artistResolver.Object,
                breakerRegistry: breakerRegistry.Object);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                OpenAIApiKey = "sk-test",
                RecommendationMode = RecommendationMode.Artists,
                RequireMbids = true,
                MaxRecommendations = 2,
                EnableIterativeRefinement = false
            };

            var result = await orchestrator.FetchRecommendationsAsync(settings);

            Assert.Single(result);
            Assert.Equal("ArtistWithMBID", result[0].Artist);
        }

        private sealed class PassThroughCircuitBreaker : ICircuitBreaker
        {
            public string ResourceName => "test";
            public CircuitState State => CircuitState.Closed;
            public DateTime LastStateChange => DateTime.UtcNow;
            public int ConsecutiveFailures => 0;
            public double FailureRate => 0;

            public event EventHandler<CircuitBreakerEventArgs> CircuitOpened { add { } remove { } }
            public event EventHandler<CircuitBreakerEventArgs> CircuitClosed { add { } remove { } }

            public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
                => operation();

            public async Task<T> ExecuteWithFallbackAsync<T>(Func<Task<T>> operation, T fallbackValue, CancellationToken cancellationToken = default)
            {
                try
                {
                    return await operation();
                }
                catch
                {
                    return fallbackValue;
                }
            }

            public CircuitBreakerStatistics GetStatistics() => new()
            {
                ResourceName = ResourceName,
                State = State,
                ConsecutiveFailures = 0,
                FailureRate = 0,
                TotalOperations = 0,
                LastStateChange = LastStateChange,
                NextHalfOpenAttempt = null,
                RecentOperations = null
            };

            public void Reset() { }
        }
    }
}
