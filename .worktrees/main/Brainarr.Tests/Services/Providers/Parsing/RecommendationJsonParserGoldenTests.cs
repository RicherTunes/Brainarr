using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using FluentAssertions;
using NLog;
using Xunit;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace Brainarr.Tests.Services.Providers.Parsing
{
    public class RecommendationJsonParserGoldenTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        public void Parser_Then_Sanitizer_MatchesGoldenNormalizedOutput()
        {
            // Arrange
            var inputPath = Path.Combine("Brainarr.Tests", "TestData", "golden_input_recs.json");
            var expectedPath = Path.Combine("Brainarr.Tests", "TestData", "golden_normalized_recs.json");
            inputPath = File.Exists(inputPath) ? inputPath : Path.Combine("TestData", "golden_input_recs.json");
            expectedPath = File.Exists(expectedPath) ? expectedPath : Path.Combine("TestData", "golden_normalized_recs.json");

            var inputJson = File.ReadAllText(inputPath);
            var expectedJson = File.ReadAllText(expectedPath);

            // Act
            var parsed = RecommendationJsonParser.Parse(inputJson, _logger);
            var sanitizer = new RecommendationSanitizer(_logger);
            var sanitized = sanitizer.SanitizeRecommendations(parsed);

            var actualProjection = sanitized.Select(r => new
            {
                artist = r.Artist,
                album = r.Album,
                reason = r.Reason,
                confidence = r.Confidence
            }).ToArray();

            var actualJson = JsonSerializer.Serialize(actualProjection, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            // Normalize line endings for cross-platform consistency
            string Normalize(string s) => s.Replace("\r\n", "\n").Trim();

            // Assert
            Normalize(actualJson).Should().Be(Normalize(expectedJson));
        }
    }
}
