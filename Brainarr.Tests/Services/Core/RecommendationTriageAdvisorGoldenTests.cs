using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class RecommendationTriageAdvisorGoldenTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Snapshot")]
        public void Analyze_GoldenSnapshot_ShouldRemainStable()
        {
            var advisor = new RecommendationTriageAdvisor();
            var settings = new BrainarrSettings
            {
                MinConfidence = 0.55,
                RequireMbids = true,
                RecommendationMode = RecommendationMode.SpecificAlbums
            };

            var items = new List<ReviewQueueService.ReviewItem>
            {
                new()
                {
                    Artist = "DupArtist",
                    Album = "DupAlbum",
                    Confidence = 0.20,
                    Reason = "already in library duplicate"
                },
                new()
                {
                    Artist = "BorderArtist",
                    Album = "BorderAlbum",
                    Confidence = 0.52
                },
                new()
                {
                    Artist = "StrongArtist",
                    Album = "StrongAlbum",
                    Confidence = 0.93,
                    ArtistMusicBrainzId = "artist-mbid",
                    AlbumMusicBrainzId = "album-mbid"
                }
            };

            var snapshot = items
                .Select(item =>
                {
                    var triage = advisor.Analyze(item, settings);
                    return new
                    {
                        artist = item.Artist,
                        album = item.Album,
                        action = triage.SuggestedAction,
                        riskScore = triage.RiskScore,
                        reasonCodes = triage.ReasonCodes
                    };
                })
                .OrderByDescending(item => item.riskScore)
                .ThenBy(item => item.artist)
                .ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var actualJson = Normalize(JsonSerializer.Serialize(snapshot, options));

            actualJson.Should().Be(ExpectedGoldenJson);
        }

        private static string Normalize(string value)
        {
            return value.Replace("\r\n", "\n");
        }

        private const string ExpectedGoldenJson =
            "[\n" +
            "  {\n" +
            "    \"artist\": \"DupArtist\",\n" +
            "    \"album\": \"DupAlbum\",\n" +
            "    \"action\": \"reject\",\n" +
            "    \"riskScore\": 9,\n" +
            "    \"reasonCodes\": [\n" +
            "      \"CONFIDENCE_BELOW_THRESHOLD\",\n" +
            "      \"CONFIDENCE_FAR_BELOW_THRESHOLD\",\n" +
            "      \"MISSING_REQUIRED_MBIDS\",\n" +
            "      \"DUPLICATE_SIGNAL\"\n" +
            "    ]\n" +
            "  },\n" +
            "  {\n" +
            "    \"artist\": \"BorderArtist\",\n" +
            "    \"album\": \"BorderAlbum\",\n" +
            "    \"action\": \"review\",\n" +
            "    \"riskScore\": 4,\n" +
            "    \"reasonCodes\": [\n" +
            "      \"CONFIDENCE_BELOW_THRESHOLD\",\n" +
            "      \"MISSING_REQUIRED_MBIDS\"\n" +
            "    ]\n" +
            "  },\n" +
            "  {\n" +
            "    \"artist\": \"StrongArtist\",\n" +
            "    \"album\": \"StrongAlbum\",\n" +
            "    \"action\": \"accept\",\n" +
            "    \"riskScore\": 0,\n" +
            "    \"reasonCodes\": [\n" +
            "      \"CONSISTENT_SIGNALS\"\n" +
            "    ]\n" +
            "  }\n" +
            "]";
    }
}
