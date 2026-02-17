using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Triage;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationTriageAdvisorTests
    {
        [Fact]
        public void Analyze_ShouldSuggestReject_ForLowConfidenceDuplicateSignals()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.5,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.2,
                Reason = "already in library duplicate"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("reject");
            result.RiskScore.Should().BeGreaterOrEqualTo(6);
            result.Reasons.Should().Contain(x => x.Contains("duplicate"));
            result.ReasonCodes.Should().Contain(TriageReasonCodes.DuplicateSignal);
            result.DetailedReasons.Should().Contain(x => x.Weight > 0);
        }

        [Fact]
        public void Analyze_ShouldSuggestAccept_ForHighConfidenceWithMbids()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.6,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.93,
                ArtistMusicBrainzId = "artist-mbid",
                AlbumMusicBrainzId = "album-mbid"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("accept");
            result.ConfidenceBand.Should().Be("high");
            result.ReasonCodes.Should().Contain(TriageReasonCodes.ConsistentSignals);
            result.RiskScore.Should().Be(0);
        }

        [Fact]
        public void Analyze_ShouldIncludeNegativeWeightReason_WhenHighConfidenceOffsetsRisk()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.95,
                RequireMbids = false
            };

            var item = new ReviewQueueService.ReviewItem
            {
                Artist = "A",
                Album = "B",
                Confidence = 0.91,
                ArtistMusicBrainzId = "artist-mbid"
            };

            var result = advisor.Analyze(item, settings);

            result.SuggestedAction.Should().Be("accept");
            result.ReasonCodes.Should().Contain(TriageReasonCodes.HighConfidenceWithMbid);
            result.DetailedReasons.Should().Contain(x => x.Weight < 0);
        }
    }
}
