using System;
using System.Linq;
using FluentAssertions;
using NLog;
using Xunit;
using Brainarr.Tests.Helpers;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace Brainarr.Tests.Services.Providers.Parsing
{
    public class RecommendationJsonParserInvariantTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        public void ParserAndSanitizer_EnforceKeyInvariants_OnNoisyInputs()
        {
            // Arrange: mixed types, missing fields, casing variants, stray citations
            var noisy = @"{
                ""recommendations"": [
                    { ""artist"": "" Artist  One  "", ""album"": ""  Debut  "" , ""confidence"": ""0.95"" },
                    { ""ARTIST"": ""Two"", ""ALBUM"": 123, ""confidence"": 2.5 },
                    { ""a"": ""Three"", ""l"": "" EP "" , ""confidence"": -10 },
                    { ""artist"": """", ""album"": ""No Artist"" },
                    { ""album"": ""No Artist"" },
                    { ""artist"": ""<b>Four</b>"", ""album"": ""<i>Record</i>"" }
                ]
            }";

            // Act
            var parsed = RecommendationJsonParser.Parse(noisy, _logger);
            var sanitizer = new RecommendationSanitizer(_logger);
            var sanitized = sanitizer.SanitizeRecommendations(parsed);

            // Assert: only entries with non-empty artist remain
            sanitized.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Artist));

            // Confidence clamped into [0,1]
            sanitized.Should().OnlyContain(r => r.Confidence >= 0.0 && r.Confidence <= 1.0);

            // No raw HTML left in key string fields
            sanitized.Should().OnlyContain(r =>
                NoHtml(r.Artist) && NoHtml(r.Album) && NoHtml(r.Genre) && NoHtml(r.Reason));

            // Whitespace collapsed (no double spaces at ends)
            sanitized.Should().OnlyContain(r =>
                !HasDoubleSpaces(r.Artist) && !HasDoubleSpaces(r.Album));
        }

        private static bool NoHtml(string? s)
            => string.IsNullOrEmpty(s) || (!s.Contains("<") && !s.Contains(">"));

        private static bool HasDoubleSpaces(string? s)
            => (s ?? string.Empty).Contains("  ");
    }
}
