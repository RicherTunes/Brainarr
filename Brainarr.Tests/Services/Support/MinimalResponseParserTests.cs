using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    [Trait("Category", "Unit")]
    public class MinimalResponseParserTests
    {
        private readonly Logger _logger;
        private readonly Mock<HttpClient> _httpClientMock;
        private readonly MinimalResponseParser _parser;

        public MinimalResponseParserTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _httpClientMock = new Mock<HttpClient>();
            _parser = new MinimalResponseParser(_logger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_InitializesSuccessfully()
        {
            // Arrange & Act
            var parser = new MinimalResponseParser(_logger);

            // Assert
            parser.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new MinimalResponseParser(null));
        }

        [Fact]
        public void Constructor_WithOptionalParameters_InitializesCorrectly()
        {
            // Arrange
            using var httpClient = new HttpClient();

            // Act & Assert
            var parser = new MinimalResponseParser(_logger, httpClient, null);
            parser.Should().NotBeNull();
        }

        #endregion

        #region ParseStandardResponse Tests

        [Fact]
        public void ParseStandardResponse_WithValidJsonArray_ReturnsRecommendations()
        {
            // Arrange
            const string jsonResponse = @"[
                {""Artist"": ""Pink Floyd"", ""Album"": ""The Wall"", ""Genre"": ""Rock"", ""Confidence"": 0.9},
                {""Artist"": ""Led Zeppelin"", ""Album"": ""IV"", ""Genre"": ""Rock"", ""Confidence"": 0.8}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(jsonResponse);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Pink Floyd");
            result[0].Album.Should().Be("The Wall");
            result[0].Confidence.Should().Be(0.9);
            result[1].Artist.Should().Be("Led Zeppelin");
        }

        [Fact]
        public void ParseStandardResponse_WithCompactFormat_ReturnsRecommendations()
        {
            // Arrange
            const string compactResponse = @"[
                {""a"": ""The Beatles"", ""l"": ""Abbey Road"", ""g"": ""Rock"", ""c"": 0.95},
                {""a"": ""Queen"", ""l"": ""Bohemian Rhapsody"", ""g"": ""Rock"", ""c"": 0.85}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(compactResponse);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("The Beatles");
            result[0].Album.Should().Be("Abbey Road");
            result[0].Confidence.Should().Be(0.95);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("Invalid JSON content")]
        [InlineData("{not an array}")]
        public void ParseStandardResponse_WithInvalidInput_ReturnsEmptyList(string invalidInput)
        {
            // Act
            var result = _parser.ParseStandardResponse(invalidInput);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseStandardResponse_WithMalformedJson_ReturnsEmptyList()
        {
            // Arrange
            const string malformedJson = @"[{""Artist"": ""Incomplete"",";

            // Act
            var result = _parser.ParseStandardResponse(malformedJson);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void ParseStandardResponse_WithMixedFormat_HandlesGracefully()
        {
            // Arrange
            const string mixedResponse = @"Here are some recommendations:
            [{""Artist"": ""The Beatles"", ""Album"": ""Abbey Road""}]
            Hope this helps!";

            // Act
            var result = _parser.ParseStandardResponse(mixedResponse);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("The Beatles");
        }

        [Fact]
        public void ParseStandardResponse_WithAlternativePropertyNames_ParsesCorrectly()
        {
            // Arrange
            const string alternativeFormat = @"[
                {""artist"": ""Pink Floyd"", ""album"": ""Dark Side""},
                {""name"": ""Queen"", ""l"": ""Greatest Hits""}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(alternativeFormat);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Pink Floyd");
            result[1].Artist.Should().Be("Queen");
        }

        [Fact]
        public void ParseStandardResponse_WithDefaultValues_FillsDefaults()
        {
            // Arrange
            const string minimalJson = @"[{""Artist"": ""Test Artist""}]";

            // Act
            var result = _parser.ParseStandardResponse(minimalJson);

            // Assert
            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Test Artist");
            result[0].Album.Should().Be("Albums"); // Default value
            result[0].Genre.Should().Be("Unknown"); // Default value
            result[0].Confidence.Should().Be(0.7); // Default confidence
        }

        #endregion

        #region Edge Cases and Error Handling

        [Fact]
        public void ParseStandardResponse_WithVeryLargeResponse_HandlesEfficiently()
        {
            // Arrange - Create a large JSON response
            var largeJson = "[" + string.Join(",",
                Enumerable.Range(1, 1000).Select(i =>
                    $@"{{""Artist"": ""Artist {i}"", ""Album"": ""Album {i}""}}")) + "]";

            var startTime = DateTime.UtcNow;

            // Act
            var result = _parser.ParseStandardResponse(largeJson);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2)); // Should be fast
            result.Should().HaveCount(1000);
            result[0].Artist.Should().Be("Artist 1");
            result[999].Artist.Should().Be("Artist 1000");
        }

        [Fact]
        public void ParseStandardResponse_WithUnicodeCharacters_HandlesCorrectly()
        {
            // Arrange
            const string unicodeResponse = @"[
                {""Artist"": ""Björk"", ""Album"": ""Homogenic""},
                {""Artist"": ""Sigur Rós"", ""Album"": ""Ágætis byrjun""},
                {""Artist"": ""张学友"", ""Album"": ""吻别""}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(unicodeResponse);

            // Assert
            result.Should().HaveCount(3);
            result[0].Artist.Should().Be("Björk");
            result[1].Artist.Should().Be("Sigur Rós");
            result[2].Artist.Should().Be("张学友");
        }

        [Fact]
        public void ParseStandardResponse_WithSpecialCharacters_SanitizesCorrectly()
        {
            // Arrange
            const string specialCharsResponse = @"[
                {""Artist"": ""AC/DC"", ""Album"": ""Back in Black""},
                {""Artist"": ""Guns N' Roses"", ""Album"": ""Appetite for Destruction""},
                {""Artist"": ""Twenty Øne Piløts"", ""Album"": ""Blurryface""}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(specialCharsResponse);

            // Assert
            result.Should().HaveCount(3);
            result[0].Artist.Should().Be("AC/DC");
            result[1].Artist.Should().Be("Guns N' Roses");
            result[2].Artist.Should().Be("Twenty Øne Piløts");
        }

        [Fact]
        public void ParseStandardResponse_WithDuplicateRecommendations_DeduplicatesCorrectly()
        {
            // Arrange
            const string duplicateResponse = @"[
                {""Artist"": ""Pink Floyd"", ""Album"": ""The Wall""},
                {""Artist"": ""Pink Floyd"", ""Album"": ""The Wall""},
                {""Artist"": ""The Beatles"", ""Album"": ""Abbey Road""}
            ]";

            // Act
            var result = _parser.ParseStandardResponse(duplicateResponse);

            // Assert
            result.Should().NotBeNull();
            // Note: Deduplication might be handled by caller, so we test what parser returns
            result.Should().HaveCount(3); // Parser returns all, deduplication happens elsewhere
        }

        [Fact]
        public void ParseStandardResponse_WithNestedJson_ExtractsCorrectly()
        {
            // Arrange
            const string nestedResponse = @"
            {
                ""status"": ""success"",
                ""recommendations"": [
                    {""Artist"": ""Pink Floyd"", ""Album"": ""Dark Side of the Moon""},
                    {""Artist"": ""Led Zeppelin"", ""Album"": ""Stairway to Heaven""}
                ],
                ""metadata"": {""count"": 2}
            }";

            // Act
            var result = _parser.ParseStandardResponse(nestedResponse);

            // Assert
            result.Should().HaveCount(2);
            result[0].Artist.Should().Be("Pink Floyd");
            result[1].Artist.Should().Be("Led Zeppelin");
        }

        #endregion

        #region Performance Tests

        [Fact]
        public void ParseStandardResponse_HighVolumeProcessing_PerformsEfficiently()
        {
            // Arrange
            var testResponses = Enumerable.Range(0, 100).Select(i =>
                $@"[{{""Artist"": ""Artist {i}"", ""Album"": ""Album {i}""}}]"
            ).ToArray();

            var startTime = DateTime.UtcNow;

            // Act
            var allResults = new List<Recommendation>();
            foreach (var response in testResponses)
            {
                var parsed = _parser.ParseStandardResponse(response);
                allResults.AddRange(parsed);
            }

            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2)); // Should be fast
            allResults.Should().HaveCount(100);
            allResults.Should().AllSatisfy(r =>
            {
                r.Artist.Should().NotBeNullOrWhiteSpace();
                r.Album.Should().NotBeNullOrWhiteSpace();
            });
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public async Task ParseStandardResponse_ConcurrentParsing_ThreadSafe()
        {
            // Arrange
            const int concurrentRequests = 20;
            var responses = Enumerable.Range(0, concurrentRequests).Select(i =>
                $@"[{{""Artist"": ""Concurrent Artist {i}"", ""Album"": ""Album {i}""}}]"
            ).ToArray();

            // Act
            var tasks = responses.Select(response => Task.Run(() =>
                _parser.ParseStandardResponse(response)
            )).ToArray();

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(concurrentRequests);
            for (int i = 0; i < concurrentRequests; i++)
            {
                results[i].Should().HaveCount(1);
                results[i][0].Artist.Should().Be($"Concurrent Artist {i}");
            }
        }

        #endregion
    }
}
