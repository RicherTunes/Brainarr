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

        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void Parse_StringNumericFields_UseInvariantCulture_RegardlessOfAmbientCulture(string cultureName)
        {
            // Regression (#12): double.TryParse / int.TryParse without an explicit culture read the
            // ambient decimal separator. Under de-DE/fr-FR (comma decimal), an LLM's string "0.95"
            // parsed as 95.0 (period treated as a thousands separator), silently corrupting the
            // confidence before the sanitizer clamped it to 1.0 — masking the real value.
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
                var json = @"{ ""recommendations"": [ { ""artist"": ""A"", ""album"": ""B"", ""confidence"": ""0.95"", ""year"": ""1999"" } ] }";

                var parsed = RecommendationJsonParser.Parse(json, _logger);

                parsed.Should().ContainSingle();
                parsed[0].Confidence.Should().BeApproximately(0.95, 0.0001);
                parsed[0].Year.Should().Be(1999);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        private static bool NoHtml(string? s)
            => string.IsNullOrEmpty(s) || (!s.Contains("<") && !s.Contains(">"));

        private static bool HasDoubleSpaces(string? s)
            => (s ?? string.Empty).Contains("  ");
    }
}
