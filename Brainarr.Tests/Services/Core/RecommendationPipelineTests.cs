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
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
        public async Task ProcessAsync_OutOfRangeYear_DoesNotDiscardBatch()
        {
            // Regression (CRITICAL): an out-of-range LLM Year hit `new DateTime(Year,1,1)` and threw
            // ArgumentOutOfRangeException; the broad orchestrator catch then discarded the ENTIRE
            // recommendation batch. Out-of-range years must be tolerated (no throw), valid recs kept.
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings { MaxRecommendations = 5, RecommendationMode = RecommendationMode.SpecificAlbums };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Good", Album = "Valid", Year = 1999 },
                    new Recommendation { Artist = "Bad", Album = "GarbageYear", Year = 50000 }, // out of DateTime range
                    new Recommendation { Artist = "AlsoBad", Album = "ZeroYear", Year = 0 }
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
                dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()))
                    .Returns<List<ImportListItemInfo>>(lst => lst);

                var items = await pipeline.ProcessAsync(
                    settings,
                    recs,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                // Pre-fix this threw ArgumentOutOfRangeException and the whole batch was lost.
                Assert.Equal(3, items.Count);
                Assert.Contains(items, i => i.Artist == "Good");
                Assert.Contains(items, i => i.Artist == "Bad");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_BaseDuplicates_UsesBothDedupAndLibraryFilters()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
                dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
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
                dupFilter.Verify(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeastOnce);
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
                dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
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
                dupFilter.Verify(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()), Times.AtLeast(2));
                topUp.VerifyAll();
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        [Fact]
        public async Task ProcessAsync_TopUp_ArtistMode_MergesResults()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
                dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            Mock<IDuplicateFilterService> dupFilter,
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
            var dupFilter = new Mock<IDuplicateFilterService>();
            dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
               .Returns((List<ImportListItemInfo> items) => items);
            dupFilter.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
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
                dupFilter.Object,
                validator.Object,
                gates.Object,
                topUp.Object,
                mbids.Object,
                artists.Object,
                dedup.Object,
                metrics,
                history);

            return (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp);
        }

        [Fact]
        public async Task ProcessAsync_AtTarget_DoesNotInvokeTopUp()
        {
            var (pipeline, dupFilter, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
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

        #region Style Filtering Tests

        [Fact]
        public async Task ProcessAsync_StyleFilter_RemovesNonMatchingGenres()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(CreateMatchingStyleCatalog());
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 3,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = new[] { "rock", "metal" }
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Rock Artist", Album = "Album A", Genre = "rock" },
                    new Recommendation { Artist = "Jazz Artist", Album = "Album B", Genre = "jazz" },
                    new Recommendation { Artist = "Metal Artist", Album = "Album C", Genre = "metal" }
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
                    // Library covers the selected styles → exercises the library-aligned filter path
                    // (not style-seeded discovery, which intentionally skips the filter).
                    new LibraryProfile { TopGenres = new Dictionary<string, int> { ["rock"] = 10 } },
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                // Jazz should be filtered out
                Assert.Equal(2, items.Count);
                Assert.DoesNotContain(items, i => i.Artist == "Jazz Artist");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_StyleFilter_PassesItemsWithNoGenre()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(CreateMatchingStyleCatalog());
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = new[] { "rock" }
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Unknown Artist", Album = "Album A", Genre = null },
                    new Recommendation { Artist = "Rock Artist", Album = "Album B", Genre = "rock" }
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

                // Both should pass - null genre passes through
                Assert.Equal(2, items.Count);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_StyleFilter_HandlesCommaDelimitedGenres()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(CreateMatchingStyleCatalog());
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = new[] { "rock" }
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Multi-Genre Artist", Album = "Album A", Genre = "rock, electronic" },
                    new Recommendation { Artist = "Pure Jazz Artist", Album = "Album B", Genre = "jazz, blues" }
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
                    // Library covers the selected style → exercises the library-aligned filter path.
                    new LibraryProfile { TopGenres = new Dictionary<string, int> { ["rock"] = 10 } },
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                // Multi-genre with rock should pass, pure jazz should be filtered
                Assert.Single(items);
                Assert.Equal("Multi-Genre Artist", items[0].Artist);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_StyleFilter_NoFilters_PassesAll()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(CreateMatchingStyleCatalog());
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = null // No filters
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Any Artist", Album = "Album A", Genre = "jazz" },
                    new Recommendation { Artist = "Other Artist", Album = "Album B", Genre = "classical" }
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

                // All should pass when no filters configured
                Assert.Equal(2, items.Count);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_StyleFilter_EmptyFilters_PassesAll()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(CreateMatchingStyleCatalog());
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = Array.Empty<string>() // Empty array
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Any Artist", Album = "Album A", Genre = "jazz" },
                    new Recommendation { Artist = "Other Artist", Album = "Album B", Genre = "classical" }
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

                // All should pass when filters array is empty
                Assert.Equal(2, items.Count);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_StyleFilter_RelaxedMode_UsesRelaxedMatching()
        {
            var styleCatalog = new Mock<IStyleCatalogService>();
            styleCatalog.Setup(s => s.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns<IEnumerable<string>>(slugs => new HashSet<string>(slugs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase));
            styleCatalog.Setup(s => s.IsMatch(It.IsAny<ICollection<string>>(), It.IsAny<ISet<string>>(), true))
                .Returns(true); // Relaxed mode matches
            styleCatalog.Setup(s => s.IsMatch(It.IsAny<ICollection<string>>(), It.IsAny<ISet<string>>(), false))
                .Returns(false); // Strict mode doesn't match

            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipelineWithStyleCatalog(styleCatalog.Object);
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = new[] { "rock" },
                    RelaxStyleMatching = true
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Prog Artist", Album = "Album A", Genre = "prog-rock" }
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
                    // Library covers the selected style → not style-seeded, so the relaxed filter runs.
                    new LibraryProfile { TopGenres = new Dictionary<string, int> { ["rock"] = 10 } },
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                // Should match with relaxed mode
                Assert.Single(items);
                styleCatalog.Verify(s => s.IsMatch(It.IsAny<ICollection<string>>(), It.IsAny<ISet<string>>(), true), Times.AtLeastOnce);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        [Fact]
        public async Task ProcessAsync_NoStyleCatalog_SkipsStyleFiltering()
        {
            // Use the original CreatePipeline which doesn't include style catalog
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    StyleFilters = new[] { "rock" } // Filters configured but no catalog
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Jazz Artist", Album = "Album A", Genre = "jazz" }
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

                // Should pass since no style catalog is present
                Assert.Single(items);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        private static IStyleCatalogService CreateMatchingStyleCatalog()
        {
            var mock = new Mock<IStyleCatalogService>();
            mock.Setup(s => s.Normalize(It.IsAny<IEnumerable<string>>()))
                .Returns<IEnumerable<string>>(slugs => new HashSet<string>(slugs ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase));
            mock.Setup(s => s.IsMatch(It.IsAny<ICollection<string>>(), It.IsAny<ISet<string>>(), It.IsAny<bool>()))
                .Returns<ICollection<string>, ISet<string>, bool>((genres, selected, relax) =>
                {
                    // Simple matching: true if any genre is in selected set
                    return genres.Any(g => selected.Contains(g));
                });
            return mock.Object;
        }

        private static (RecommendationPipeline pipeline,
            Mock<IDuplicateFilterService> dupFilter,
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
        CreatePipelineWithStyleCatalog(IStyleCatalogService styleCatalog)
        {
            var logger = Helpers.TestLogger.CreateNullLogger();
            var lib = new Mock<ILibraryAnalyzer>();
            var dupFilter = new Mock<IDuplicateFilterService>();
            dupFilter.Setup(l => l.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
               .Returns((List<ImportListItemInfo> items) => items);
            dupFilter.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
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
                dupFilter.Object,
                validator.Object,
                gates.Object,
                topUp.Object,
                mbids.Object,
                artists.Object,
                dedup.Object,
                metrics,
                history,
                styleCatalog);

            return (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp);
        }

        #endregion

        #region Wave 11C Audit Gap Coverage

        // Wave 11C audit gap: filter rejection paths must not silently surface invalid
        // recommendations. When validator rejects everything and top-up is disabled,
        // the pipeline must return an empty list without invoking enrichment.
        [Fact]
        public async Task PipelineFilterRejection_ValidatorRejectsAll_ReturnsEmpty_AndSkipsEnrichment()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 3,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off // critical: disables top-up so we see the true rejected-all outcome
                };
                var input = new List<Recommendation>
                {
                    new Recommendation { Artist = "Fake1", Album = "Hallucinated", Confidence = 0.1 },
                    new Recommendation { Artist = "Fake2", Album = "Imaginary",   Confidence = 0.2 }
                };
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = new List<Recommendation>(),       // none survived validation
                        FilteredRecommendations = input,                         // all rejected
                        FilterDetails = input.ToDictionary(r => $"{r.Artist} - {r.Album}", _ => "hallucination"),
                        TotalCount = input.Count,
                        ValidCount = 0,
                        FilteredCount = input.Count
                    });

                var items = await pipeline.ProcessAsync(
                    settings,
                    input,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                Assert.Empty(items);
                // Enrichment must not be called with an empty/rejected set spending API calls
                mbids.Verify(m => m.EnrichWithMbidsAsync(
                    It.Is<List<Recommendation>>(l => l.Count > 0),
                    It.IsAny<CancellationToken>()), Times.Never);
                // Top-up must not fire when refinement is disabled, even at zero results
                topUp.Verify(t => t.TopUpAsync(
                    It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(),
                    It.IsAny<LibraryProfile>(), It.IsAny<int>(),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: partial validation rejection — only the validated subset
        // must reach enrichment and the final import list. Rejected items must NOT leak
        // through (silent acceptance bug).
        [Fact]
        public async Task PipelineFilterRejection_PartialRejection_OnlyValidSubsetReachesEnrichment()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 5,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off
                };
                var validRec = new Recommendation { Artist = "RealArtist", Album = "RealAlbum", Confidence = 0.9 };
                var rejectedRec1 = new Recommendation { Artist = "FakeArtist", Album = "FakeAlbum (AI Imagined)", Confidence = 0.1 };
                var rejectedRec2 = new Recommendation { Artist = "Bot", Album = "(reimagined)", Confidence = 0.2 };
                var input = new List<Recommendation> { validRec, rejectedRec1, rejectedRec2 };

                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = new List<Recommendation> { validRec },
                        FilteredRecommendations = new List<Recommendation> { rejectedRec1, rejectedRec2 },
                        TotalCount = input.Count,
                        ValidCount = 1,
                        FilteredCount = 2
                    });

                List<Recommendation> capturedForEnrichment = null;
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, ct) =>
                    {
                        capturedForEnrichment = lst.ToList();
                        return Task.FromResult(lst);
                    });

                var items = await pipeline.ProcessAsync(
                    settings,
                    input,
                    new LibraryProfile(),
                    new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(),
                    CancellationToken.None);

                // Exactly the valid one reaches enrichment — partial rejection respected
                Assert.NotNull(capturedForEnrichment);
                Assert.Single(capturedForEnrichment);
                Assert.Equal("RealArtist", capturedForEnrichment[0].Artist);
                // And only the valid one survives to the final list
                Assert.Single(items);
                Assert.Equal("RealArtist", items[0].Artist);
                Assert.DoesNotContain(items, i => i.Artist == "FakeArtist" || i.Artist == "Bot");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: pipeline composition order. Existing
        // ProcessAsync_TopUp_Then_Dedup_Then_FilterDuplicates_Order asserts the
        // tail-end ordering (history -> dedup -> lib) but does NOT assert that
        // validation happens BEFORE the library pre-filter and BEFORE enrichment.
        // This pins the pre-enrichment stage ordering: Validate -> FilterExisting -> Enrich -> Gate.
        [Fact]
        public async Task PipelineComposition_PreEnrichmentStages_RunInExpectedOrder()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 },
                    new Recommendation { Artist = "C", Album = "D", Confidence = 0.8 }
                };

                var order = new List<string>();
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Callback((List<Recommendation> _, bool __) => order.Add("validate"))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = recs,
                        FilteredRecommendations = new List<Recommendation>(),
                        TotalCount = recs.Count,
                        ValidCount = recs.Count,
                        FilteredCount = 0
                    });
                dupFilter.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                    .Callback((List<Recommendation> _, bool __) => order.Add("filter-existing"))
                    .Returns((List<Recommendation> r, bool _) => r);
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Callback((List<Recommendation> _, CancellationToken __) => order.Add("enrich"))
                    .Returns<List<Recommendation>, CancellationToken>((r, _) => Task.FromResult(r));
                gates.Setup(g => g.ApplySafetyGates(
                        It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(),
                        It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(),
                        It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                        It.IsAny<CancellationToken>()))
                    .Callback(() => order.Add("gate"))
                    .Returns<List<Recommendation>, BrainarrSettings, ReviewQueueService, RecommendationHistory, Logger, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics, CancellationToken>(
                        (r, _, __, ___, ____, _____, ______) => r);

                await pipeline.ProcessAsync(
                    settings, recs, new LibraryProfile(), new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                // Pre-enrichment stages must run in declared order. Reading the source
                // pins: ValidateBatch (line 80) -> FilterExistingRecommendations (line 87)
                // -> Enrich (line 135/139) -> ApplySafetyGates (line 143).
                Assert.Equal(new[] { "validate", "filter-existing", "enrich", "gate" }, order.ToArray());
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: partial-results graceful degradation. When BackfillStrategy.Off
        // and validator yields fewer than MaxRecommendations, the pipeline must return
        // what it has WITHOUT invoking top-up. Existing sibling tests verify top-up is
        // invoked under target with Standard strategy; this verifies the opposite path.
        [Fact]
        public async Task PipelinePartialResults_BackfillOff_UnderTarget_ReturnsWithoutTopUp()
        {
            var (pipeline, dupFilter, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 10, // ask for 10
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off // but disable refinement
                };
                // Only 2 candidates available
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Only1", Album = "Album1", Confidence = 0.9 },
                    new Recommendation { Artist = "Only2", Album = "Album2", Confidence = 0.8 }
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
                    settings, recs, new LibraryProfile(), new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                // Graceful partial result: 2 of 10 returned without throwing
                Assert.Equal(2, items.Count);
                Assert.True(items.Count < settings.MaxRecommendations);
                // Top-up must not fire when refinement is disabled
                topUp.Verify(t => t.TopUpAsync(
                    It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(),
                    It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(),
                    It.IsAny<LibraryProfile>(), It.IsAny<int>(),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: empty input handling. Pipeline must not throw on
        // empty/zero-candidate input — it should run validator (which returns empty),
        // skip enrichment, and return an empty list. Models the case where the
        // upstream provider returned nothing at all.
        [Fact]
        public async Task PipelineEmptyInput_NoRecommendations_ReturnsEmpty_NoThrow()
        {
            var (pipeline, dupFilter, validator, _, topUp, mbids, _, dedup, _, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 5,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off
                };
                var empty = new List<Recommendation>();
                validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), false))
                    .Returns(new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                    {
                        ValidRecommendations = empty,
                        FilteredRecommendations = empty,
                        TotalCount = 0,
                        ValidCount = 0,
                        FilteredCount = 0
                    });

                var items = await pipeline.ProcessAsync(
                    settings, empty, new LibraryProfile(), new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                Assert.NotNull(items);
                Assert.Empty(items);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: validator error classification. The pipeline must
        // distinguish "input invalid / validator rejected" (handled gracefully,
        // see PipelineFilterRejection_*) from "downstream resolver threw"
        // (must surface to the caller for retry / circuit-break logic upstream).
        // Today the pipeline does NOT swallow enrichment exceptions — pin it.
        [Fact]
        public async Task PipelineErrorClassification_EnrichmentThrows_BubblesException()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "A", Album = "B", Confidence = 0.9 }
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
                // Simulate a downstream timeout / transient network error from the
                // MBID resolver. This is categorically different from validator
                // rejection — the pipeline should NOT silently treat it as zero
                // valid recommendations; it must propagate so the caller can react.
                var simulated = new TimeoutException("simulated MusicBrainz timeout");
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(simulated);

                var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
                {
                    await pipeline.ProcessAsync(
                        settings, recs, new LibraryProfile(), new ReviewQueueService(logger, tmp),
                        Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                        Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);
                });
                Assert.Same(simulated, ex);
                // Safety gates and dedup must NOT have been run when enrichment fails;
                // we should not pretend to have a (partially) valid result.
                gates.Verify(g => g.ApplySafetyGates(
                    It.IsAny<List<Recommendation>>(), It.IsAny<BrainarrSettings>(), It.IsAny<ReviewQueueService>(),
                    It.IsAny<RecommendationHistory>(), It.IsAny<Logger>(),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                dedup.Verify(d => d.DeduplicateRecommendations(It.IsAny<List<ImportListItemInfo>>()), Times.Never);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Wave 11C audit gap: null-fallback contract in pipeline.cs line 87-88
        // ("?? validationSummary.ValidRecommendations"). If a misbehaving
        // IDuplicateFilterService returns null instead of a list, the pipeline
        // must fall back to the validated set rather than NRE — otherwise a
        // single bad implementation crashes the whole import list run.
        [Fact]
        public async Task PipelineNullFallback_FilterExistingReturnsNull_FallsBackToValidatedList()
        {
            var (pipeline, dupFilter, validator, gates, topUp, mbids, artists, dedup, metrics, history, logger, tmp) = CreatePipeline();
            try
            {
                var settings = new BrainarrSettings
                {
                    MaxRecommendations = 2,
                    RecommendationMode = RecommendationMode.SpecificAlbums,
                    BackfillStrategy = BackfillStrategy.Off
                };
                var recs = new List<Recommendation>
                {
                    new Recommendation { Artist = "Survivor1", Album = "AlbumA", Confidence = 0.9 },
                    new Recommendation { Artist = "Survivor2", Album = "AlbumB", Confidence = 0.8 }
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
                // Misbehaving dependency: returns null
                dupFilter.Setup(l => l.FilterExistingRecommendations(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                    .Returns((List<Recommendation>)null);

                // Capture what reaches enrichment to confirm the validated list was
                // used as the null-fallback (not an empty list).
                List<Recommendation> reachedEnrichment = null;
                mbids.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                    .Returns<List<Recommendation>, CancellationToken>((lst, _) =>
                    {
                        reachedEnrichment = lst.ToList();
                        return Task.FromResult(lst);
                    });

                var items = await pipeline.ProcessAsync(
                    settings, recs, new LibraryProfile(), new ReviewQueueService(logger, tmp),
                    Mock.Of<IAIProvider>(p => p.ProviderName == "OpenAI"),
                    Mock.Of<ILibraryAwarePromptBuilder>(), CancellationToken.None);

                // Did not throw: graceful null-handling honored
                Assert.NotNull(items);
                Assert.NotNull(reachedEnrichment);
                Assert.Equal(2, reachedEnrichment.Count); // fell back to validated list
                Assert.Equal(2, items.Count);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        #endregion
    }
}
