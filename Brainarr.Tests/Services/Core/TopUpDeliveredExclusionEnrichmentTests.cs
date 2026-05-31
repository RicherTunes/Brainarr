using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// T1 + T2 (live-log finding): the iterative top-up did not contribute under the user's
    /// RequireMbids config because of two compounding gaps —
    ///  T1: the already-delivered (initial-batch) set was never threaded into the top-up prompt's
    ///      [[SYSTEM_AVOID]] list or the strategy's dedup baseline, so the provider re-emitted
    ///      delivered artists (counted "new" because they are not in the LIBRARY, then dropped post-hoc);
    ///  T2: top-up recs arrive from the provider WITHOUT MBIDs and were never enrichment-resolved on the
    ///      top-up path, so TopUpPlanner's require-MBID filter dropped them all.
    /// These tests pin both gaps and their honest accounting.
    /// </summary>
    public class TopUpDeliveredExclusionEnrichmentTests
    {
        private static Recommendation Rec(string artist, string album = "", string mbid = null) =>
            new Recommendation { Artist = artist, Album = album ?? string.Empty, Confidence = 0.9, ArtistMusicBrainzId = mbid };

        // ---------- T1: strategy seeds the delivered set into avoid-list + dedup baseline ----------

        [Fact]
        [Trait("Category", "IterativeStrategy")]
        public async Task Strategy_SeedsAlreadyProvided_IntoAvoidList_AndExcludesThemFromResults()
        {
            var prompts = new List<string>();
            var provider = new Mock<IAIProvider>();
            // Provider re-emits a delivered artist (Slowdive) plus genuinely new ones.
            List<Recommendation> Returned() => new List<Recommendation> { Rec("Slowdive"), Rec("Swervedriver"), Rec("Ride") };
            provider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((p, ct) => prompts.Add(p))
                .ReturnsAsync(Returned);
            provider.Setup(x => x.GetRecommendationsAsync(It.IsAny<string>()))
                .Callback<string>(p => prompts.Add(p))
                .ReturnsAsync(Returned);

            var promptBuilder = new Mock<ILibraryAwarePromptBuilder>();
            promptBuilder.Setup(x => x.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(), It.IsAny<List<Artist>>(), It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns("BASE PROMPT");
            promptBuilder.Setup(x => x.EstimateTokens(It.IsAny<string>(), It.IsAny<string>())).Returns(10);

            var strategy = new IterativeRecommendationStrategy(LogManager.GetCurrentClassLogger(), promptBuilder.Object);
            var settings = new BrainarrSettings { MaxRecommendations = 2, Provider = AIProvider.Ollama, RecommendationMode = RecommendationMode.Artists };

            var result = await strategy.GetIterativeRecommendationsAsync(
                provider.Object, new LibraryProfile(), new List<Artist>(), new List<Album>(),
                settings, shouldRecommendArtists: true,
                alreadyProvided: new List<Recommendation> { Rec("Slowdive") });

            // T1a: the delivered artist reaches the [[SYSTEM_AVOID]] marker on the very first iteration.
            prompts.Should().NotBeEmpty();
            prompts[0].Should().Contain("[[SYSTEM_AVOID:");
            prompts[0].Should().Contain("Slowdive");

            // T1b: the delivered artist is excluded from the results (dedup baseline), new ones survive.
            result.Should().NotContain(r => string.Equals(r.Artist, "Slowdive", StringComparison.OrdinalIgnoreCase));
            result.Should().Contain(r => r.Artist == "Swervedriver");
        }

        // ---------- T2 + integration: TopUpPlanner enriches before the MBID filter ----------

        private static (TopUpPlanner planner, Mock<IAIProvider> provider, Mock<ILibraryAnalyzer> analyzer,
            Mock<ILibraryAwarePromptBuilder> prompt, Mock<IDuplicationPrevention> dedup, Mock<IArtistMbidResolver> resolver) Build()
        {
            var filter = new Mock<IDuplicateFilterService>();
            filter.Setup(f => f.FilterDuplicates(It.IsAny<List<ImportListItemInfo>>()))
                .Returns((List<ImportListItemInfo> items) => items ?? new List<ImportListItemInfo>());
            var planner = new TopUpPlanner(LogManager.GetCurrentClassLogger(), filter.Object);

            var analyzer = new Mock<ILibraryAnalyzer>();
            analyzer.Setup(a => a.GetAllArtists()).Returns(new List<Artist>());
            analyzer.Setup(a => a.GetAllAlbums()).Returns(new List<Album>());

            var prompt = new Mock<ILibraryAwarePromptBuilder>();
            prompt.Setup(p => p.BuildLibraryAwarePrompt(
                It.IsAny<LibraryProfile>(), It.IsAny<List<Artist>>(), It.IsAny<List<Album>>(),
                It.IsAny<BrainarrSettings>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns("prompt");
            prompt.Setup(p => p.EstimateTokens(It.IsAny<string>(), It.IsAny<string>())).Returns(10);

            var dedup = new Mock<IDuplicationPrevention>();
            dedup.Setup(d => d.FilterPreviouslyRecommended(It.IsAny<List<ImportListItemInfo>>(), It.IsAny<ISet<string>>()))
                .Returns((List<ImportListItemInfo> items, ISet<string> allow) => items ?? new List<ImportListItemInfo>());

            return (planner, new Mock<IAIProvider>(), analyzer, prompt, dedup, new Mock<IArtistMbidResolver>());
        }

        [Fact]
        [Trait("Category", "TopUp")]
        public async Task TopUp_EnrichesTopUpRecs_SoResolvableArtistSurvivesMbidFilter()
        {
            var (planner, provider, analyzer, prompt, dedup, resolver) = Build();

            // Provider returns two NEW artists, neither carrying an MBID (as real GLM output never does).
            List<Recommendation> Returned() => new List<Recommendation> { Rec("Swervedriver"), Rec("Chapterhouse") };
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Returned);
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>())).ReturnsAsync(Returned);

            // T2: the resolver assigns an MBID to the resolvable artist, leaves the other unresolvable.
            resolver.Setup(r => r.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<Recommendation> recs, CancellationToken ct) =>
                    recs.Select(r => r.Artist == "Swervedriver" ? r with { ArtistMusicBrainzId = "mbid-swerve" } : r).ToList());

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 10,
                RecommendationMode = RecommendationMode.Artists,
                RequireMbids = true
            };

            var result = await planner.TopUpAsync(
                settings, provider.Object, analyzer.Object, prompt.Object, dedup.Object,
                new LibraryProfile(), needed: 2, initialValidation: null, cancellationToken: CancellationToken.None,
                alreadyAccepted: null, artistResolver: resolver.Object);

            // The resolvable artist survives the require-MBID filter; the unresolvable one is correctly dropped.
            result.Should().ContainSingle();
            result[0].Artist.Should().Be("Swervedriver");
            result[0].ArtistMusicBrainzId.Should().Be("mbid-swerve");
            resolver.Verify(r => r.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        [Trait("Category", "TopUp")]
        public async Task TopUp_ExcludesAlreadyDelivered_AndReturnsOnlyResolvedNewArtists()
        {
            var (planner, provider, analyzer, prompt, dedup, resolver) = Build();

            // Provider re-emits a delivered artist (Slowdive) + a new resolvable one (Swervedriver).
            List<Recommendation> Returned() => new List<Recommendation> { Rec("Slowdive"), Rec("Swervedriver") };
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Returned);
            provider.Setup(p => p.GetRecommendationsAsync(It.IsAny<string>())).ReturnsAsync(Returned);

            resolver.Setup(r => r.EnrichArtistsAsync(It.IsAny<List<Recommendation>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<Recommendation> recs, CancellationToken ct) =>
                    recs.Select(r => r with { ArtistMusicBrainzId = "mbid-" + r.Artist }).ToList());

            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Ollama,
                MaxRecommendations = 10,
                RecommendationMode = RecommendationMode.Artists,
                RequireMbids = true
            };

            var result = await planner.TopUpAsync(
                settings, provider.Object, analyzer.Object, prompt.Object, dedup.Object,
                new LibraryProfile(), needed: 2, initialValidation: null, cancellationToken: CancellationToken.None,
                alreadyAccepted: new List<ImportListItemInfo> { new ImportListItemInfo { Artist = "Slowdive" } },
                artistResolver: resolver.Object);

            // T1: the delivered Slowdive is excluded; only the genuinely-new, now-resolved artist returns.
            result.Should().NotContain(i => string.Equals(i.Artist, "Slowdive", StringComparison.OrdinalIgnoreCase));
            result.Should().Contain(i => i.Artist == "Swervedriver");
        }
    }
}
