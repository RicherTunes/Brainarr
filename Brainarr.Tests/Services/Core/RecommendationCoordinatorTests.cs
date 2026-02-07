using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationCoordinatorTests
    {
        private static (RecommendationCoordinator coord,
            Mock<IRecommendationCache> cache,
            Mock<IRecommendationPipeline> pipeline,
            Mock<IRecommendationSanitizer> sanitizer,
            Mock<IRecommendationSchemaValidator> schema,
            RecommendationHistory history,
            Mock<ILibraryProfileService> profiles,
            Logger logger,
            string tmp)
        Create()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var cache = new Mock<IRecommendationCache>();
            var pipeline = new Mock<IRecommendationPipeline>();
            var sanitizer = new Mock<IRecommendationSanitizer>();
            var schema = new Mock<IRecommendationSchemaValidator>();
            var tmp = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var history = new RecommendationHistory(logger, tmp);
            var profiles = new Mock<ILibraryProfileService>();
            profiles.Setup(p => p.GetLibraryProfile()).Returns(new LibraryProfile
            {
                TopGenres = new Dictionary<string, int> { { "rock", 10 }, { "jazz", 5 } },
                TopArtists = new List<string> { "A", "B" }
            });

            var keyBuilder = new RecommendationCacheKeyBuilder(new DefaultPlannerVersionProvider());
            var coord = new RecommendationCoordinator(logger, cache.Object, pipeline.Object, sanitizer.Object, schema.Object, history, profiles.Object, keyBuilder);
            return (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp);
        }

        [Fact]
        public async Task RunAsync_CacheHit_ReturnsCached_WithoutPipeline()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                var cachedItems = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "A", Album = "B" } };
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out cachedItems)).Returns(true);

                var fetchCalled = 0;
                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct)
                {
                    fetchCalled++;
                    return new List<Recommendation>();
                }

                var usedKeys = new List<string>();
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((key, _, __) => usedKeys.Add(key));

                var result = await coord.RunAsync(
                    new BrainarrSettings(),
                    Fetch,
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Single(result);
                Assert.Equal(0, fetchCalled);
                pipeline.Verify(p => p.ProcessAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task RunAsync_CacheMiss_CallsPipeline_AndStoresCache()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                // miss
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                         .Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>()))
                      .Returns(new SanitizationReport { TotalItems = 1 });

                var pipelineResult = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "X", Album = "Y" } };
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<List<Recommendation>>(),
                        It.IsAny<LibraryProfile>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(pipelineResult);

                var fetchCalled = 0;
                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct)
                {
                    fetchCalled++;
                    return new List<Recommendation> { new Recommendation { Artist = "X", Album = "Y" } };
                }

                var result = await coord.RunAsync(
                    new BrainarrSettings(),
                    Fetch,
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Single(result);
                Assert.Equal(1, fetchCalled);
                cache.Verify(c => c.Set(It.IsAny<string>(), pipelineResult, It.IsAny<TimeSpan?>()), Times.Once);
                pipeline.VerifyAll();
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task RunAsync_UsesLibraryProfileCache_BetweenCalls()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                string keyFromSet = null;
                cache.SetupSequence(c => c.TryGet(It.IsAny<string>(), out notUsed))
                     .Returns(false) // first call miss
                     .Returns(true); // second call hit (after Set)
                // Key capture for TryGet is not required for this test; rely on sequence above
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keyFromSet = k);
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

                profiles.Setup(p => p.GetLibraryProfile()).Returns(new LibraryProfile());

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct)
                {
                    return new List<Recommendation>();
                }

                var settings = new BrainarrSettings();
                var rv1 = await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                var rv2 = await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                profiles.Verify(p => p.GetLibraryProfile(), Times.AtLeastOnce()); // service provides profile each run (it may cache internally)
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_RecommendationMode_And_Model()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                // Always miss to force Set and capture keys
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                         .Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
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
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => Task.FromResult(new List<Recommendation>());

                var s1 = new BrainarrSettings { RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "model-a" };
                var s2 = new BrainarrSettings { RecommendationMode = RecommendationMode.Artists, ModelSelection = "model-a" };
                var s3 = new BrainarrSettings { RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "model-b" };

                await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s3, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(3, keys.Count);
                Assert.Equal(3, new HashSet<string>(keys).Count); // all distinct
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Stable_For_Same_Settings()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                // Always miss
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
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => Task.FromResult(new List<Recommendation>());

                var s = new BrainarrSettings { RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "same-model", MaxRecommendations = 7 };
                await coord.RunAsync(s, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.Equal(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_DiscoveryMode()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
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
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => Task.FromResult(new List<Recommendation>());

                var s1 = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Adjacent, RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "m" };
                var s2 = new BrainarrSettings { DiscoveryMode = DiscoveryMode.Exploratory, RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "m" };

                await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.NotEqual(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_Provider()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var keys = new List<string>();
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

                var s1 = new BrainarrSettings { Provider = AIProvider.OpenAI, ModelSelection = "m" };
                var s2 = new BrainarrSettings { Provider = AIProvider.Anthropic, ModelSelection = "m" };

                await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.NotEqual(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_MaxRecommendations()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var keys = new List<string>();
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

                var s1 = new BrainarrSettings { MaxRecommendations = 10, ModelSelection = "m" };
                var s2 = new BrainarrSettings { MaxRecommendations = 20, ModelSelection = "m" };

                await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.NotEqual(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Removed EffectiveModel variant test: EffectiveModel is a read-only alias of ModelSelection.

        [Fact]
        public async Task CacheKey_Stable_With_Unordered_Profile_Contents()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var keys = new List<string>();
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                    .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                var lp1 = new LibraryProfile
                {
                    TopGenres = new Dictionary<string, int> { { "jazz", 2 }, { "rock", 5 }, { "blues", 1 } },
                    TopArtists = new List<string> { "Z", "A", "M" }
                };
                var lp2 = new LibraryProfile
                {
                    TopGenres = new Dictionary<string, int> { { "rock", 5 }, { "blues", 1 }, { "jazz", 2 } },
                    TopArtists = new List<string> { "M", "Z", "A" }
                };
                profiles.SetupSequence(p => p.GetLibraryProfile())
                    .Returns(lp1)
                    .Returns(lp2);

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();
                var settings = new BrainarrSettings { ModelSelection = "m" };
                await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                // Profile caching is now in ILibraryProfileService - the mock's SetupSequence handles returning lp1 then lp2
                await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.Equal(keys[0], keys[1]); // ordering differences should not affect the key
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_SamplingStrategy()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var keys = new List<string>();
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => keys.Add(k));

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();

                var s1 = new BrainarrSettings { SamplingStrategy = SamplingStrategy.Balanced, RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "m" };
                var s2 = new BrainarrSettings { SamplingStrategy = SamplingStrategy.Comprehensive, RecommendationMode = RecommendationMode.SpecificAlbums, ModelSelection = "m" };

                await coord.RunAsync(s1, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord.RunAsync(s2, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.NotEqual(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task RunAsync_CacheHit_Using_PreviouslySetKey_SkipsPipeline_OnSecondCall()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                // First run: miss, process, then Set
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>()))
                         .Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>()))
                      .Returns(new SanitizationReport { TotalItems = 0 });

                var pipelineResult = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Hit", Album = "Cache" } };
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<List<Recommendation>>(),
                        It.IsAny<LibraryProfile>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(pipelineResult);

                string savedKey = null;
                cache.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<System.TimeSpan?>()))
                     .Callback<string, List<ImportListItemInfo>, System.TimeSpan?>((k, _, __) => savedKey = k);

                Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct)
                    => Task.FromResult(new List<Recommendation> { new Recommendation { Artist = "Hit", Album = "Cache" } });

                var settings = new BrainarrSettings { ModelSelection = "m" };
                var rq1 = new ReviewQueueService(logger, tmp);
                var result1 = await coord.RunAsync(settings, Fetch, rq1, Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                Assert.Single(result1);
                Assert.False(string.IsNullOrWhiteSpace(savedKey));

                // Second run: configure a hit only when the same key is used
                var cachedItems = new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Hit", Album = "Cache" } };
                cache.Reset();
                // Default to miss
                List<ImportListItemInfo> outList;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out outList)).Returns(false);
                // Return true for the saved key
                cache.Setup(c => c.TryGet(savedKey, out cachedItems)).Returns(true);

                var rq2 = new ReviewQueueService(logger, tmp);
                var result2 = await coord.RunAsync(settings, Fetch, rq2, Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Single(result2);
                // Ensure pipeline was not invoked for second run
                pipeline.Verify(p => p.ProcessAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<LibraryProfile>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<CancellationToken>()), Times.Once); // only from first run
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task CacheKey_Varies_By_LibraryProfile()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var cache1 = new Mock<IRecommendationCache>();
            var pipeline1 = new Mock<IRecommendationPipeline>();
            var sanitizer1 = new Mock<IRecommendationSanitizer>();
            var schema1 = new Mock<IRecommendationSchemaValidator>();
            var lib1 = new Mock<ILibraryAnalyzer>();

            var cache2 = new Mock<IRecommendationCache>();
            var pipeline2 = new Mock<IRecommendationPipeline>();
            var sanitizer2 = new Mock<IRecommendationSanitizer>();
            var schema2 = new Mock<IRecommendationSchemaValidator>();
            var lib2 = new Mock<ILibraryAnalyzer>();

            var tmp = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var history = new RecommendationHistory(logger, tmp);

            try
            {
                // Always miss so Set is called
                List<ImportListItemInfo> notUsed;
                cache1.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                cache2.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer1.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                sanitizer2.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema1.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                schema2.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline1.Setup(p => p.ProcessAsync(It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());
                pipeline2.Setup(p => p.ProcessAsync(It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                lib1.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile
                {
                    TopGenres = new Dictionary<string, int> { { "rock", 10 }, { "jazz", 5 } },
                    TopArtists = new List<string> { "A", "B" }
                });
                lib2.Setup(l => l.AnalyzeLibrary()).Returns(new LibraryProfile
                {
                    TopGenres = new Dictionary<string, int> { { "electronic", 3 }, { "classical", 2 } },
                    TopArtists = new List<string> { "X", "Y" }
                });

                var keys = new List<string>();
                cache1.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>()))
                      .Callback<string, List<ImportListItemInfo>, TimeSpan?>((k, _, __) => keys.Add(k));
                cache2.Setup(c => c.Set(It.IsAny<string>(), It.IsAny<List<ImportListItemInfo>>(), It.IsAny<TimeSpan?>()))
                      .Callback<string, List<ImportListItemInfo>, TimeSpan?>((k, _, __) => keys.Add(k));

                var profiles1 = new Mock<ILibraryProfileService>();
                profiles1.Setup(p => p.GetLibraryProfile()).Returns(lib1.Object.AnalyzeLibrary());
                var profiles2 = new Mock<ILibraryProfileService>();
                profiles2.Setup(p => p.GetLibraryProfile()).Returns(lib2.Object.AnalyzeLibrary());
                var keyBuilder = new RecommendationCacheKeyBuilder(new DefaultPlannerVersionProvider());
                var coord1 = new RecommendationCoordinator(logger, cache1.Object, pipeline1.Object, sanitizer1.Object, schema1.Object, history, profiles1.Object, keyBuilder);
                var coord2 = new RecommendationCoordinator(logger, cache2.Object, pipeline2.Object, sanitizer2.Object, schema2.Object, history, profiles2.Object, keyBuilder);

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();
                var settings = new BrainarrSettings { ModelSelection = "m", RecommendationMode = RecommendationMode.SpecificAlbums };

                await coord1.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                await coord2.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.Equal(2, keys.Count);
                Assert.NotEqual(keys[0], keys[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task LibraryProfileCache_Expires_After_Ttl_ReanalysesLibrary()
        {
            var (coord, cache, pipeline, sanitizer, schema, history, profiles, logger, tmp) = Create();
            try
            {
                List<ImportListItemInfo> notUsed;
                cache.Setup(c => c.TryGet(It.IsAny<string>(), out notUsed)).Returns(false);
                sanitizer.Setup(s => s.SanitizeRecommendations(It.IsAny<List<Recommendation>>())).Returns<List<Recommendation>>(r => r);
                schema.Setup(s => s.Validate(It.IsAny<List<Recommendation>>())).Returns(new SanitizationReport { TotalItems = 0 });
                pipeline.Setup(p => p.ProcessAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<List<Recommendation>>(), It.IsAny<LibraryProfile>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<IAIProvider>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                profiles.Setup(p => p.GetLibraryProfile()).Returns(new LibraryProfile());

                async Task<List<Recommendation>> Fetch(LibraryProfile p, CancellationToken ct) => new List<Recommendation>();
                var settings = new BrainarrSettings();

                // First run populates cache
                await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                profiles.Verify(p => p.GetLibraryProfile(), Times.Once);

                // Second run should request profile again (service handles its own TTL)

                await coord.RunAsync(settings, Fetch, new ReviewQueueService(logger, tmp), Mock.Of<IAIProvider>(), Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                profiles.Verify(p => p.GetLibraryProfile(), Times.Exactly(2));
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}
