using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationPipelineTests
    {
        [Fact]
        public async Task ProcessAsync_Deduplicates_FinalItems()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Dup", Album = "Same" },
                    new Recommendation { Artist = "Dup", Album = "Same" },
                    new Recommendation { Artist = "Unique", Album = "Once" }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                // When ConvertToImportListItems runs, it will convert 3 items; then dedup should drop down to 2
                dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                    .Returns<List<ImportListItemInfo>>(lst =>
                    {
                        return lst.GroupBy(x => ($"{x.Artist}|{x.Album}").ToLowerInvariant())
                                  .Select(g => g.First()).ToList();
                    })
                    .Verifiable();

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Equal(2, items.Count);
                dedup.Verify(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeastOnce);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_BaseDuplicates_UsesBothDedupAndLibraryFilters()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "X", Album = "Y" },
                    new Recommendation { Artist = "x", Album = "y" }, // case-diff duplicate
                    new Recommendation { Artist = "Z", Album = "W" }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                // Make library filter drop exact duplicates (case-insensitive)
                lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                   .Returns<List<ImportListItemInfo>>(lst => lst
                        .GroupBy(i => ($"{i.Artist}|{i.Album}").ToLowerInvariant())
                        .Select(g => g.First()).ToList())
                   .Verifiable();

                // Also run session de-dup (idempotent given above) and verify it's called
                dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                    .Returns<List<ImportListItemInfo>>(lst => lst)
                    .Verifiable();

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Equal(2, items.Count); // reduced from 3 -> 2
                lib.Verify(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeastOnce);
                dedup.Verify(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeastOnce);
                topUp.Verify(t => t.TopUpAsync(
                    It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_ArtistMode_Gates_Dedups_And_TopUps_ToTarget()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 4, RecommendationMode = RecommendationMode.Artists, BackfillStrategy = BackfillStrategy.Standard };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A" },
                    new Recommendation { Artist = "A" } // duplicate pre-validation
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), true))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                // Ensure artist resolver is used (not album MBID)
                artists.Setup(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) => Task.FromResult(lst))
                    .Verifiable();
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(new List<Recommendation>()))
                    .Verifiable();

                // Gates pass-through but verify called
                gates.Setup(g => g.ApplySafetyGates(
                        It.IsAny<List<Recommendation>>(),
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<RecommendationHistory>(),
                        It.IsAny<Logger>(),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics, CancellationToken>((enriched, _, __, ___, ____, _____, ______) => enriched)
                    .Verifiable();

                // Make library filter & dedup idempotent (we're testing invocation and final count)
                lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                   .Returns<List<ImportListItemInfo>>(x => x)
                   .Verifiable();
                dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                    .Returns<List<ImportListItemInfo>>(x => x)
                    .Verifiable();

                // Top-up should supply the remaining 3 to reach target of 4
                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAnalyzer>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<IDuplicationPrevention>(),
                        It.IsAny<LibraryProfile>(),
                        It.Is<int>(need => need >= 1 && need <= 3),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>
                    {
                        new ImportListItemInfo { Artist = "B" },
                        new ImportListItemInfo { Artist = "C" },
                        new ImportListItemInfo { Artist = "D" }
                    })
                    .Verifiable();

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.True(items.Count >= 4);
                artists.Verify(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Once);
                mbids.Verify(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Never);
                gates.Verify(g => g.ApplySafetyGates(
                    It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(),
                    It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(), It.IsAny<CancellationToken>()), Times.Once);
                // With top-up, both dedup and lib filters should be called at least twice (base + after top-up)
                dedup.Verify(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeast(2));
                lib.Verify(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeast(2));
                topUp.VerifyAll();
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        [Fact]
        public async Task ProcessAsync_TopUp_ArtistMode_MergesResults()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 3, RecommendationMode = RecommendationMode.Artists };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A" }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), true))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                // artists resolver used
                artists.Setup(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) => Task.FromResult(lst));

                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAnalyzer>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<IDuplicationPrevention>(),
                        It.IsAny<LibraryProfile>(),
                        It.Is<int>(need => need == 2),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>
                    {
                        new ImportListItemInfo { Artist = "B" },
                        new ImportListItemInfo { Artist = "C" }
                    })
                    .Verifiable();

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.True(items.Count >= 3);
                topUp.VerifyAll();
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        [Fact]
        public async Task ProcessAsync_ArtistMode_UsesArtistResolver_NotAlbumResolver()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.Artists };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "" },
                    new Recommendation { Artist = "B", Album = "" }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), true))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                artists.Setup(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) => Task.FromResult(lst))
                    .Verifiable();
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) => Task.FromResult(lst));

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                artists.Verify(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_AlbumMode_UsesMbidResolver_NotArtistResolver()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B" },
                    new Recommendation { Artist = "C", Album = "D" }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) => Task.FromResult(lst))
                    .Verifiable();

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                mbids.Verify(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_SafetyGate_DropsItems()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 1, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B" },
                    new Recommendation { Artist = "C", Album = "D" },
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                // Gate to only first item
                gates.Setup(g => g.ApplySafetyGates(
                        It.IsAny<List<Recommendation>>(),
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<RecommendationHistory>(),
                        It.IsAny<Logger>(),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics, CancellationToken>((lst, s, q, h, lg, m, ct) => new List<Recommendation> { lst[0] });

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Single(items);
                topUp.Verify(t => t.TopUpAsync(It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_TopUp_Then_Dedup_Then_FilterDuplicates_Order()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation> { new Recommendation { Artist = "X", Album = "Y" } };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                // Gate away all items to force top-up
                gates.Setup(g => g.ApplySafetyGates(It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(), It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(), It.IsAny<CancellationToken>()))
                    .Returns(new List<Recommendation>());

                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "A", Album = "B" }, new ImportListItemInfo { Artist = "C", Album = "D" } });

                var callOrder = new List<string>();
                dedup.Setup(d => d.FilterPreviouslyRecommended(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<ISet<string>>()))
                    .Callback((List<ImportListItemInfo> _, ISet<string> __) => callOrder.Add("history"))
                    .Returns((List<ImportListItemInfo> items, ISet<string> _) => items);
                dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                    .Callback((List<ImportListItemInfo> _) => callOrder.Add("dedup"))
                    .Returns<List<ImportListItemInfo>>(lst => lst);
                lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                    .Callback((List<ImportListItemInfo> _) => callOrder.Add("lib"))
                    .Returns<List<ImportListItemInfo>>(lst => lst);
                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.True(items.Count >= 2);
                Assert.Contains("history", callOrder);
                Assert.Contains("dedup", callOrder);
                Assert.Contains("lib", callOrder);
                Assert.True(callOrder.Count >= 3, "Expected history, dedup, and library stages.");
                var historyIndex = callOrder.LastIndexOf("history");
                var dedupIndex = callOrder.LastIndexOf("dedup");
                var libIndex = callOrder.LastIndexOf("lib");
                Assert.True(historyIndex < dedupIndex, "History filter should run before session dedup.");
                Assert.True(dedupIndex < libIndex, "Session dedup should run before library filtering.");
                var finalPair = callOrder.TakeLast(2).ToArray();
                Assert.Equal("dedup", finalPair[0]);
                Assert.Equal("lib", finalPair[1]);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        [Fact]
        public async Task ProcessAsync_TopUp_StillUnderTarget_ExecutesWarningPath()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 3, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation> { new Recommendation { Artist = "A", Album = "B" } };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                // Return no top-up items to stay under target
                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.True(items.Count < settings.MaxRecommendations);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        private static (RecommendationPipeline pipeline,
            Mock<ILibraryAnalyzer> lib,
            Mock<IRecommendationValidator> validator,
            Mock<ISafetyGateService> gates,
            Mock<ITopUpPlanner> topUp,
            Mock<IMusicBrainzResolver> mbids,
            Mock<IArtistMbidResolver> artists,
            Mock<IDuplicationPrevention> dedup,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            RecommendationHistory history,
            Logger logger,
            string tmp)
        CreatePipeline()
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var lib = new Mock<ILibraryAnalyzer>();
            lib.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
               .Returns((List<ImportListItemInfo> items) => items);
            lib.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
               .Returns((List<Recommendation> recs, bool _) => recs);
            var validator = new Mock<IRecommendationValidator>();
            var gates = new Mock<ISafetyGateService>();
            gates.Setup(g => g.ApplySafetyGates(
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<RecommendationHistory>(),
                    It.IsAny<Logger>(),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                    It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics, CancellationToken>((enriched, _, __, ___, ____, _____, ______) => enriched);
            var topUp = new Mock<ITopUpPlanner>();
            var mbids = new Mock<IMusicBrainzResolver>();
            mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, CancellationToken>((recs, ct) => Task.FromResult(recs));
            var artists = new Mock<IArtistMbidResolver>();
            artists.Setup(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .Returns<List<Recommendation>, CancellationToken>((recs, ct) => Task.FromResult(recs));
            var dedup = new Mock<IDuplicationPrevention>();
            dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items);
            dedup.Setup(d => d.FilterPreviouslyRecommended(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<ISet<string>>()))
                .Returns((List<ImportListItemInfo> items, ISet<string> _) => items);

            var metrics = new NzbDrone.Core.ImportLists.Brainarr.Performance.PerformanceMetrics(logger);
            var tmp = Path.Combine(Path.GetTempPath(), "BrainarrTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            var history = new RecommendationHistory(logger, tmp);

            var pipeline = new RecommendationPipeline(
                logger,
                lib.Object,
                validator.Object,
                gates.Object,
                topUp.Object,
                mbids.Object,
                artists.Object,
                dedup.Object,
                metrics,
                history);

            return (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp);
        }

        [Fact]
        public async Task ProcessAsync_AtTarget_DoesNotInvokeTopUp()
        {
            var (pipeline, lib, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, BackfillStrategy = BackfillStrategy.Standard };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 },
                    new Recommendation { Artist = "C", Album = "D", Confidence = 0.8 }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns<List<Recommendation>, bool>((lst, allowArtistOnly) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = lst,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = lst.Count,
                        ValidCount = lst.Count,
                        FilteredCount = 0
                    });

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Equal(2, items.Count);
                topUp.Verify(t => t.TopUpAsync(It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public async Task ProcessAsync_UnderTarget_InvokesTopUpWithDeficit()
        {
            var (pipeline, lib, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 5, BackfillStrategy = BackfillStrategy.Standard };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 },
                    new Recommendation { Artist = "C", Album = "D", Confidence = 0.8 }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns<List<Recommendation>, bool>((lst, allowArtistOnly) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = lst,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = lst.Count,
                        ValidCount = lst.Count,
                        FilteredCount = 0
                    });

                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<IAIProvider>(),
                        It.IsAny<ILibraryAnalyzer>(),
                        It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<IDuplicationPrevention>(),
                        It.IsAny<LibraryProfile>(),
                        It.IsAny<int>(),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>
                    {
                        new ImportListItemInfo { Artist = "E", Album = "F" },
                        new ImportListItemInfo { Artist = "G", Album = "H" },
                        new ImportListItemInfo { Artist = "I", Album = "J" }
                    });

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.True(items.Count >= 5);
                topUp.Verify(t => t.TopUpAsync(
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<IAIProvider>(),
                    It.IsAny<ILibraryAnalyzer>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(),
                    It.IsAny<IDuplicationPrevention>(),
                    It.IsAny<LibraryProfile>(),
                    It.Is<int>(needed => needed == 3),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
        }

        [Fact]
        public async Task ProcessAsync_CancellationEarly_ThrowsAndSkipsResolvers()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 3, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation> { new Recommendation { Artist = "A", Album = "B" } };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                var cts = new CancellationTokenSource();
                cts.Cancel();

                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                {
                    await pipeline.ProcessAsync(
                        settings,
                        recs,
                        new LibraryProfile(),
                        new ReviewQueueService(logger, tmp),
                        Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                        Mock.Of<ILibraryAwarePromptBuilder>(),
                        cts.Token);
                });
                mbids.Verify(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Never);
                artists.Verify(a => a.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.Never);
                gates.Verify(g => g.ApplySafetyGates(It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(), It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(), It.IsAny<CancellationToken>()), Times.Never);
                dedup.Verify(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()), Times.Never);
                topUp.Verify(t => t.TopUpAsync(It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_Validate_PerItem_Logging_Paths_Execute()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums, EnableDebugLogging = true };
                // Also enable per-item logs via reflection-friendly property (defaults true in pipeline call)
                settings.LogPerItemDecisions = true;
                var valid = new List<Recommendation> { new Recommendation { Artist = "Acc", Album = "One", Confidence = 0.9 } };
                var filtered = new List<Recommendation> { new Recommendation { Artist = "Rej", Album = "Two", Confidence = 0.1 }, new Recommendation { Artist = "Rej2", Album = "Three", Confidence = 0.2 } };
                var details = new Dictionary<string, string> { ["Rej - Two"] = "too new" };

                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = valid,
                        FilteredRecommendations = filtered,
                        FilterDetails = details,
                        TotalCount = valid.Count + filtered.Count,
                        ValidCount = valid.Count,
                        FilteredCount = filtered.Count
                    });

                // Ensure gating returns non-null list
                gates.Setup(g => g.ApplySafetyGates(
                        It.IsAny<List<Recommendation>>(),
                        It.IsAny<BrainarrSettings>(),
                        It.IsAny<ReviewQueueService>(),
                        It.IsAny<RecommendationHistory>(),
                        It.IsAny<Logger>(),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics, CancellationToken>((enriched, _, __, ___, ____, _____, ______) => enriched);

                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((recs, ct) => Task.FromResult(recs));

                topUp.Setup(t => t.TopUpAsync(
                        It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(),
                        It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ImportListItemInfo>());

                var items = await pipeline.ProcessAsync(
                    settings,
                    valid.Concat(filtered).ToList(),
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Single(items); // one valid
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_ConvertsYear_To_ReleaseDate()
        {
            var (pipeline, lib, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 2, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B", Year = 1999 },
                    new Recommendation { Artist = "C", Album = "D" } // no year
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Equal(2, items.Count);
                Assert.Equal(new DateTime(1999, 1, 1), items[0].ReleaseDate);
                Assert.Equal(DateTime.MinValue, items[1].ReleaseDate);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}
