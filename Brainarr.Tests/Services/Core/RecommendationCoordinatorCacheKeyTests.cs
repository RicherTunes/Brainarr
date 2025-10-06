using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Parser.Model;
using Xunit;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationCoordinatorCacheKeyTests
    {
        private static (RecommendationCoordinator coord,
            Mock<IRecommendationCache> cache,
            Mock<IRecommendationPipeline> pipeline,
            Mock<IRecommendationSanitizer> sanitizer,
            Mock<IRecommendationSchemaValidator> schema,
            RecommendationHistory history,
            Mock<ILibraryProfileService> profiles,
            Logger logger)
        Create()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var cache = new Mock<IRecommendationCache>();
            var pipeline = new Mock<IRecommendationPipeline>();
            var sanitizer = new Mock<IRecommendationSanitizer>();
            var schema = new Mock<IRecommendationSchemaValidator>();
            var history = new RecommendationHistory(logger);
            var profiles = new Mock<ILibraryProfileService>();
            profiles.Setup(p => p.GetLibraryProfile()).Returns(new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "rock", 10 }, { "jazz", 5 } },
                TopArtists = new List<string> { "A", "B", "C" }
            });

            var keyBuilder = new RecommendationCacheKeyBuilder(new DefaultPlannerVersionProvider());
            var coord = new RecommendationCoordinator(logger, cache.Object, pipeline.Object, sanitizer.Object, schema.Object, history, profiles.Object, keyBuilder);
            return (coord, cache, pipeline, sanitizer, schema, history, profiles, logger);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task CacheKey_Stable_When_StyleFilters_Reordered()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger) = Create();
            List<ImportListItemInfo> notUsed;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
            sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                     .Returns<List<Recommendation>>(r => r);
            schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>()))
                  .Returns(new SanitizationReport { TotalItems = 0 });
            pipeline.Setup(p => p.ProcessAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ImportListItemInfo>());

            var keys = new List<string>();
            cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>()))
                 .Callback<string, List<ImportListItemInfo>, TimeSpan?>((k, _, __) => keys.Add(k));

            async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

            var s1 = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ModelSelection = "qwen2.5:latest",
                DiscoveryMode = DiscoveryMode.Similar,
                SamplingStrategy = SamplingStrategy.Balanced,
                MaxRecommendations = 10,
                StyleFilters = new[] { "shoegaze", "dreampop", "ambient" },
                RelaxStyleMatching = true
            };

            var s2 = new BrainarrSettings
            {
                Provider = s1.Provider,
                ModelSelection = s1.ModelSelection,
                DiscoveryMode = s1.DiscoveryMode,
                SamplingStrategy = s1.SamplingStrategy,
                MaxRecommendations = s1.MaxRecommendations,
                StyleFilters = new[] { "ambient", "shoegaze", "dreampop" },
                RelaxStyleMatching = s1.RelaxStyleMatching
            };

            await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
            await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

            Assert.Equal(2, keys.Count);
            Assert.Equal(keys[0], keys[1]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task CacheKey_Changes_When_MaxSelectedStyles_Changes()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger) = Create();
            List<ImportListItemInfo> notUsed;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
            sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                     .Returns<List<Recommendation>>(r => r);
            schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>()))
                  .Returns(new SanitizationReport { TotalItems = 0 });
            pipeline.Setup(p => p.ProcessAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ImportListItemInfo>());

            var keys = new List<string>();
            cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>()))
                 .Callback<string, List<ImportListItemInfo>, TimeSpan?>((k, _, __) => keys.Add(k));

            async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

            var sA = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ModelSelection = "qwen2.5:latest",
                MaxRecommendations = 10,
                MaxSelectedStyles = 1,
                StyleFilters = new[] { "shoegaze", "dreampop" }
            };

            var sB = new BrainarrSettings
            {
                Provider = sA.Provider,
                ModelSelection = sA.ModelSelection,
                MaxRecommendations = sA.MaxRecommendations,
                MaxSelectedStyles = 3,
                StyleFilters = sA.StyleFilters
            };

            await coord.RunAsync(sA, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
            await coord.RunAsync(sB, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

            Assert.Equal(2, keys.Count);
            Assert.NotEqual(keys[0], keys[1]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task CacheKey_Stable_When_StyleFilters_CaseAndOrder_Differ()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger) = Create();
            List<ImportListItemInfo> notUsed;
            cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
            sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                     .Returns<List<Recommendation>>(r => r);
            schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>()))
                  .Returns(new SanitizationReport { TotalItems = 0 });
            pipeline.Setup(p => p.ProcessAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ImportListItemInfo>());

            var keys = new List<string>();
            cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>()))
                 .Callback<string, List<ImportListItemInfo>, TimeSpan?>((k, _, __) => keys.Add(k));

            async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

            var s1 = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                ModelSelection = "qwen2.5:latest",
                StyleFilters = new[] { "Dream Pop", "dreampop" }
            };

            var s2 = new BrainarrSettings
            {
                Provider = s1.Provider,
                ModelSelection = s1.ModelSelection,
                StyleFilters = new[] { "DREAM pop", "dreampop" }
            };

            await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
            await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

            Assert.Equal(2, keys.Count);
            Assert.Equal(keys[0], keys[1]);
        }
    }
}
