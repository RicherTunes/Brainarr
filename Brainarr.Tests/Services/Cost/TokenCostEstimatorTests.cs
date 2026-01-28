using System;
using System.Linq;
using FluentAssertions;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Cost;
using Xunit;

namespace Brainarr.Tests.Services.Cost
{
    public class TokenCostEstimatorTests
    {
        private readonly Logger _logger;
        private readonly TokenCostEstimator _estimator;

        public TokenCostEstimatorTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _estimator = new TokenCostEstimator(_logger);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidLogger_InitializesSuccessfully()
        {
            // Arrange & Act
            var estimator = new TokenCostEstimator(_logger);

            // Assert
            estimator.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TokenCostEstimator(null));
        }

        #endregion

        #region EstimateTokenCount Tests

        [Theory]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData(null, 0)]
        public void EstimateTokenCount_WithNullOrWhitespace_ReturnsZero(string? input, int expected)
        {
            // Act
            var result = _estimator.EstimateTokenCount(input);

            // Assert
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("Hello", 4)] // 1 word + overhead ≈ 4 tokens
        [InlineData("Hello world", 6)] // 2 words + overhead ≈ 6 tokens
        [InlineData("The Beatles are a great band", 10)] // 6 words + overhead ≈ 10 tokens
        public void EstimateTokenCount_WithSimpleText_ReturnsReasonableEstimate(string input, int expectedApproximate)
        {
            // Act
            var result = _estimator.EstimateTokenCount(input);

            // Assert
            result.Should().BeGreaterThan(0);
            result.Should().BeCloseTo(expectedApproximate, 3); // Allow ±3 token variance
        }

        [Fact]
        public void EstimateTokenCount_WithTypicalMusicPrompt_ReturnsReasonableEstimate()
        {
            // Arrange
            const string musicPrompt = @"Based on my music library containing Pink Floyd, Led Zeppelin,
                and The Beatles, recommend 5 similar progressive rock artists I might enjoy.";

            // Act
            var result = _estimator.EstimateTokenCount(musicPrompt);

            // Assert
            result.Should().BeGreaterThan(20);
            result.Should().BeLessThan(100);
        }

        [Fact]
        public void EstimateTokenCount_WithLongText_ScalesAppropriately()
        {
            // Arrange
            var shortText = "The Beatles";
            var longText = string.Join(" ", Enumerable.Repeat("The Beatles are great", 100));

            // Act
            var shortTokens = _estimator.EstimateTokenCount(shortText);
            var longTokens = _estimator.EstimateTokenCount(longText);

            // Assert
            shortTokens.Should().BeGreaterThan(0);
            longTokens.Should().BeGreaterThan(shortTokens * 50); // Should scale appropriately
        }

        [Fact]
        public void EstimateTokenCount_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            const string specialText = "Artist: AC/DC, Album: Back in Black (1980) - Rating: 9/10!";

            // Act
            var result = _estimator.EstimateTokenCount(specialText);

            // Assert
            result.Should().BeGreaterThan(10);
            result.Should().BeLessThan(30);
        }

        [Fact]
        public void EstimateTokenCount_WithJsonFormat_CountsCorrectly()
        {
            // Arrange
            const string jsonText = @"{""artist"": ""Pink Floyd"", ""album"": ""The Wall"", ""confidence"": 0.9}";

            // Act
            var result = _estimator.EstimateTokenCount(jsonText);

            // Assert
            result.Should().BeGreaterThan(15);
            result.Should().BeLessThan(40);
        }

        [Fact]
        public void EstimateTokenCount_ConsistentResults_ForSameInput()
        {
            // Arrange
            const string testText = "Recommend music similar to The Beatles and Pink Floyd";

            // Act
            var result1 = _estimator.EstimateTokenCount(testText);
            var result2 = _estimator.EstimateTokenCount(testText);
            var result3 = _estimator.EstimateTokenCount(testText);

            // Assert
            result1.Should().Be(result2);
            result2.Should().Be(result3);
            result1.Should().BeGreaterThan(10);
        }

        #endregion

        #region Cost Estimation Tests

        [Theory]
        [InlineData(AIProvider.OpenAI, "gpt-4o-mini", 1000)]
        [InlineData(AIProvider.Anthropic, "claude-3-haiku", 500)]
        [InlineData(AIProvider.Gemini, "gemini-1.5-flash", 2000)]
        public void EstimateCost_WithValidProvider_ReturnsValidEstimate(AIProvider provider, string model, int expectedTokens)
        {
            // Arrange
            const string prompt = "Test prompt for cost estimation";

            // Act
            var result = _estimator.EstimateCost(provider, model, prompt, expectedTokens);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(provider);
            result.Model.Should().Be(model);
            result.EstimatedCost.Should().BeGreaterThanOrEqualTo(0);
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void EstimateCost_WithLocalProviders_ReturnsZeroCost(AIProvider localProvider)
        {
            // Arrange
            const string prompt = "Test prompt";

            // Act
            var result = _estimator.EstimateCost(localProvider, "local-model", prompt, 1000);

            // Assert
            result.Should().NotBeNull();
            result.EstimatedCost.Should().Be(0m, "local providers should have no API costs");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(100000)]
        public void EstimateCost_WithEdgeTokenCounts_HandlesGracefully(int responseTokens)
        {
            // Arrange
            const string prompt = "Test prompt";

            // Act
            var result = _estimator.EstimateCost(AIProvider.OpenAI, "gpt-4o-mini", prompt, responseTokens);

            // Assert
            result.Should().NotBeNull();
            result.EstimatedCost.Should().BeGreaterThanOrEqualTo(0, "cost should never be negative");
        }

        #endregion

        #region Provider Pricing Tests

        [Theory]
        [InlineData(AIProvider.OpenAI, "gpt-4o-mini")]
        [InlineData(AIProvider.Anthropic, "claude-3-haiku")]
        [InlineData(AIProvider.Gemini, "gemini-1.5-flash")]
        [InlineData(AIProvider.Groq, "llama-3.1-70b")]
        [InlineData(AIProvider.DeepSeek, "deepseek-chat")]
        [InlineData(AIProvider.Perplexity, "llama-3.1-sonar-large")]
        [InlineData(AIProvider.OpenRouter, "anthropic/claude-3-haiku")]
        public void EstimateCost_AllCloudProviders_HaveValidPricing(AIProvider provider, string model)
        {
            // Arrange
            const string prompt = "Test pricing prompt";

            // Act
            var result = _estimator.EstimateCost(provider, model, prompt, 1000);

            // Assert
            result.Should().NotBeNull();
            result.Provider.Should().Be(provider);
            result.Model.Should().Be(model);
            result.EstimatedCost.Should().BeGreaterThanOrEqualTo(0, $"{provider} should have valid pricing");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public void EstimateTokenCount_WithVeryLongText_HandlesEfficiently()
        {
            // Arrange
            var veryLongText = string.Join(" ", Enumerable.Repeat("word", 10000));
            var startTime = DateTime.UtcNow;

            // Act
            var result = _estimator.EstimateTokenCount(veryLongText);
            var elapsed = DateTime.UtcNow - startTime;

            // Assert
            elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1)); // Should be fast
            result.Should().BeGreaterThan(1000);
        }

        [Theory]
        [InlineData("\n\n\n")]
        [InlineData("\t\t\t")]
        [InlineData("   \n\r\t   ")]
        public void EstimateTokenCount_WithWhitespaceOnly_ReturnsZero(string whitespaceInput)
        {
            // Act
            var result = _estimator.EstimateTokenCount(whitespaceInput);

            // Assert
            result.Should().Be(0);
        }

        [Fact]
        public void EstimateTokenCount_WithUnicodeText_HandlesCorrectly()
        {
            // Arrange
            const string unicodeText = "推荐音乐艺术家：张学友、陈奕迅、林俊杰";

            // Act
            var result = _estimator.EstimateTokenCount(unicodeText);

            // Assert
            result.Should().BeGreaterThan(5);
            result.Should().BeLessThan(50);
        }

        #endregion
    }
}
