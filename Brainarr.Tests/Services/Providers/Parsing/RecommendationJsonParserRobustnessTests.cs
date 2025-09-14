using System;
using System.Text.Json;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Parsing
{
    public class RecommendationJsonParserRobustnessTests
    {
        private readonly Logger _logger = Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public void CaseInsensitiveProperties_ParseCorrectly()
        {
            var json = "{\n" +
                       "  \"ReCoMmEnDaTiOnS\": [\n" +
                       "    { \"Artist\": \"A\", \"ALBUM\": \"B\", \"Genre\": \"G\", \"Confidence\": \"0.8\", \"ReAsOn\": \"r\" }\n" +
                       "  ]\n" +
                       "}";
            var list = RecommendationJsonParser.Parse(json, _logger);
            list.Should().ContainSingle(r => r.Artist == "A" && r.Album == "B");
            list[0].Confidence.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(1);
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void Confidence_NaNOrInfinity_DefaultsToMid(string val)
        {
            var json = $"{{\"recommendations\":[{{\"artist\":\"X\",\"album\":\"Y\",\"confidence\":\"{val}\"}}]}}";
            var list = RecommendationJsonParser.Parse(json, _logger);
            list.Should().ContainSingle();
            list[0].Confidence.Should().BeApproximately(0.85, 1e-9);
        }

        [Fact]
        public void MalformedItems_AreSkipped()
        {
            var json = "[\n" +
                       "  123,\n" +
                       "  { \"artist\": \"Good\", \"album\": \"Item\", \"confidence\": 0.9 },\n" +
                       "  [1,2,3],\n" +
                       "  { \"artist\": \"\", \"album\": \"NoArtist\" }\n" +
                       "]";
            var list = RecommendationJsonParser.Parse(json, _logger);
            list.Should().ContainSingle(r => r.Artist == "Good" && r.Album == "Item");
        }

        [Fact]
        public void FallbackExtraction_FromTextBlock_Works()
        {
            var content = "Some text before\n```json\n[ { \"artist\": \"Z\", \"album\": \"Q\" } ]\n```\nAfter";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().ContainSingle(r => r.Artist == "Z" && r.Album == "Q");
        }
    }
}
