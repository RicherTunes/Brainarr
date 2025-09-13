using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationPipelineStyleGuardTests
    {
        [Fact]
        public async Task Pipeline_Should_Drop_OutOfStyle_Recommendations_When_Strict()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var libraryAnalyzer = new Mock<ILibraryAnalyzer>();
            libraryAnalyzer.Setup(l => l.FilterDuplicates(It.IsAny<List<NzbDrone.Core.Parser.Model.ImportListItemInfo>>()))
                .Returns<List<NzbDrone.Core.Parser.Model.ImportListItemInfo>>(x => x);
            var validator = new Mock<IRecommendationValidator>();
            var safety = new Mock<ISafetyGateService>();
            var planner = new Mock<ITopUpPlanner>();
            var mbid = new Mock<IMusicBrainzResolver>();
            mbid.Setup(m => m.EnrichWithMbidsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<Recommendation> recs, CancellationToken _) => recs);
            var artistResolver = new Mock<IArtistMbidResolver>();
            var dedup = new Mock<IDuplicationPrevention>();
            dedup.Setup(d => d.DeduplicateRecommendations(It.IsAny<List<NzbDrone.Core.Parser.Model.ImportListItemInfo>>()))
                .Returns<List<NzbDrone.Core.Parser.Model.ImportListItemInfo>>(x => x);
            var metrics = new Mock<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>();
            safety.Setup(g => g.ApplySafetyGates(
                    It.IsAny<List<Recommendation>>(),
                    It.IsAny<BrainarrSettings>(),
                    It.IsAny<ReviewQueueService>(),
                    It.IsAny<RecommendationHistory>(),
                    It.IsAny<Logger>(),
                    It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics>(),
                    It.IsAny<CancellationToken>()))
                .Returns((List<Recommendation> recs, BrainarrSettings s, ReviewQueueService rq, RecommendationHistory h, Logger l, NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics mm, CancellationToken ct) => recs);

            // Validation returns all input as valid
            validator.Setup(v => v.ValidateBatch(It.IsAny<List<Recommendation>>(), It.IsAny<bool>()))
                .Returns((List<Recommendation> recs, bool allowArtistOnly) => new NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult
                {
                    ValidRecommendations = recs,
                    FilteredRecommendations = new List<Recommendation>(),
                    TotalCount = recs.Count,
                    ValidCount = recs.Count,
                    FilteredCount = 0
                });

            // No top-up
            planner.Setup(p => p.TopUpAsync(It.IsAny<BrainarrSettings>(), It.IsAny<IAIProvider>(), It.IsAny<ILibraryAnalyzer>(), It.IsAny<ILibraryAwarePromptBuilder>(), It.IsAny<IDuplicationPrevention>(), It.IsAny<LibraryProfile>(), It.IsAny<int>(), It.IsAny<NzbDrone.Core.ImportLists.Brainarr.Services.ValidationResult>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<NzbDrone.Core.Parser.Model.ImportListItemInfo>());

            var history = new RecommendationHistory(logger);
            var styleCatalog = new StyleCatalogService(logger, new System.Net.Http.HttpClient());
            var pipeline = new RecommendationPipeline(logger, libraryAnalyzer.Object, validator.Object, safety.Object, planner.Object, mbid.Object, artistResolver.Object, dedup.Object, metrics.Object, history, styleCatalog);

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.OpenAI,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                BackfillStrategy = BackfillStrategy.Off,
                MaxRecommendations = 1,
                StyleFilters = new[] { "progressive-rock" },
                RelaxStyleMatching = false
            };

            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "Yes", Album = "Close to the Edge", Genre = "Progressive Rock", Confidence = 0.9 },
                new Recommendation { Artist = "Miles Davis", Album = "Kind of Blue", Genre = "Jazz", Confidence = 0.9 }
            };

            var result = await pipeline.ProcessAsync(settings, recs, new LibraryProfile(), new NzbDrone.Core.ImportLists.Brainarr.Services.Support.ReviewQueueService(logger), null, null, CancellationToken.None);

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Yes");
        }
    }
}
