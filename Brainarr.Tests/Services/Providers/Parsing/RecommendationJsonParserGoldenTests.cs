using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
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
            var inputPath = ResolveTestDataPath("golden_input_recs.json");
            var expectedPath = ResolveTestDataPath("golden_normalized_recs.json");

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

        private static string ResolveTestDataPath(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "TestData", fileName),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", fileName),
                Path.Combine("Brainarr.Tests", "TestData", fileName),
                Path.Combine("TestData", fileName)
            }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException($"Test data file not found: {fileName}");
        }
    }
}
