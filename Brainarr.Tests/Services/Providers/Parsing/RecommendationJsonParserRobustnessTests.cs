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

        // --- Truncated-array salvage (provider hit max_tokens mid-array) ----------------------

        [Fact]
        public void TruncatedArray_NoClosingBracket_SalvagesCompleteObjects()
        {
            // Mirrors a real Z.AI/GLM response: ```json fence, array opened, several complete items,
            // then the response is cut off mid-object at the token cap (no closing brace or bracket).
            var content = "```json\n[\n" +
                          "  { \"artist\": \"Radiohead\", \"album\": \"OK Computer\", \"confidence\": 0.95 },\n" +
                          "  { \"artist\": \"Portishead\", \"album\": \"Dummy\", \"confidence\": 0.9 },\n" +
                          "  { \"artist\": \"Massive Att";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
            list.Should().Contain(r => r.Artist == "Radiohead" && r.Album == "OK Computer");
            list.Should().Contain(r => r.Artist == "Portishead" && r.Album == "Dummy");
        }

        [Fact]
        public void TruncatedArray_BracesInsideStringValues_DoNotMiscount()
        {
            // A '}' inside a reason string must not be treated as an object terminator.
            var content = "[\n" +
                          "  { \"artist\": \"A\", \"album\": \"B\", \"reason\": \"uses a } and { in the title\" },\n" +
                          "  { \"artist\": \"C\", \"album\": \"D\", \"reason\": \"clean\" },\n" +
                          "  { \"artist\": \"E\", \"album\": \"F";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
            list.Should().Contain(r => r.Artist == "A" && r.Album == "B");
            list.Should().Contain(r => r.Artist == "C" && r.Album == "D");
        }

        [Fact]
        public void TruncatedArray_NestedObjects_ExtractedAtTopLevelOnly()
        {
            // Item with a nested object — salvage must extract the whole top-level item, not the inner one.
            var content = "[\n" +
                          "  { \"artist\": \"A\", \"album\": \"B\", \"meta\": { \"src\": \"x\" }, \"confidence\": 0.8 },\n" +
                          "  { \"artist\": \"C\", \"album\": \"D\", \"meta\": { \"src\": \"y\" } },\n" +
                          "  { \"artist\": \"Cut";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
            list.Should().Contain(r => r.Artist == "A");
            list.Should().Contain(r => r.Artist == "C");
        }

        [Fact]
        public void WellFormedArray_StillParsesWithoutSalvage()
        {
            // Salvage must not change behavior for valid input (only fires when results are empty).
            var content = "[ { \"artist\": \"A\", \"album\": \"B\" }, { \"artist\": \"C\", \"album\": \"D\" } ]";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
        }

        [Fact]
        public void TruncatedObjectWrappedArray_SalvagesElements()
        {
            // GLM emits both bare arrays AND object-wrapped arrays interchangeably. When the wrapped
            // form truncates, the outer { never closes — salvage must still recover the array elements
            // (the bug that produced 0 items on a completed-but-truncated response, May 2026).
            var content = "```json\n{ \"recommendations\": [\n" +
                          "  { \"artist\": \"Radiohead\", \"album\": \"OK Computer\", \"confidence\": 0.95 },\n" +
                          "  { \"artist\": \"Portishead\", \"album\": \"Dummy\", \"confidence\": 0.9 },\n" +
                          "  { \"artist\": \"Massive Att";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
            list.Should().Contain(r => r.Artist == "Radiohead" && r.Album == "OK Computer");
            list.Should().Contain(r => r.Artist == "Portishead" && r.Album == "Dummy");
        }

        [Fact]
        public void TruncatedObjectWrappedArray_WithNestedMeta_ExtractsElementNotInnerObject()
        {
            // Object-wrapped + per-item nested object + truncation: each element (incl. its nested
            // meta) is recovered whole; the inner meta object is not mistaken for a separate element.
            var content = "{ \"recommendations\": [\n" +
                          "  { \"artist\": \"A\", \"album\": \"B\", \"meta\": { \"src\": \"x\" } },\n" +
                          "  { \"artist\": \"C\", \"album\": \"D\", \"meta\": { \"src\": \"y\" } },\n" +
                          "  { \"artist\": \"Cut";
            var list = RecommendationJsonParser.Parse(content, _logger);
            list.Should().HaveCount(2);
            list.Should().Contain(r => r.Artist == "A");
            list.Should().Contain(r => r.Artist == "C");
        }
    }
}
