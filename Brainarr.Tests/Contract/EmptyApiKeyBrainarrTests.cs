using System;
using System.Threading;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Contract
{
    /// <summary>
    /// Empty API key graceful handling tests for all Brainarr providers.
    ///
    /// Verifies PROV-04 requirements:
    /// - Providers accept empty API key without throwing ArgumentException
    /// - IsConfigured property reflects API key state
    /// - GetRecommendationsAsync returns empty list for unconfigured providers
    /// - TestConnectionAsync returns false with appropriate message
    /// - All 3 providers (OpenAI, Gemini, ZaiGlm) have identical behavior
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Category", "EmptyApiKey")]
    public class EmptyApiKeyBrainarrTests
    {
        private readonly Mock<IHttpClient> _mockHttpClient;
        private readonly Logger _logger;

        public EmptyApiKeyBrainarrTests()
        {
            _mockHttpClient = new Mock<IHttpClient>();
            _logger = TestLogger.CreateNullLogger();
        }

        #region Test Factories

        /// <summary>
        /// Factory for creating providers with empty API keys.
        /// </summary>
        public static class BrainarrEmptyApiKeyTestFactory
        {
            public static ZaiGlmProvider CreateZaiGlmProviderWithEmptyApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new ZaiGlmProvider(httpClient, logger, "");
            }

            public static ZaiGlmProvider CreateZaiGlmProviderWithNullApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new ZaiGlmProvider(httpClient, logger, null);
            }

            public static ZaiGlmProvider CreateZaiGlmProviderWithValidApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new ZaiGlmProvider(httpClient, logger, "valid-zai-api-key");
            }

            public static OpenAIProvider CreateOpenAIProviderWithEmptyApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new OpenAIProvider(httpClient, logger, "");
            }

            public static OpenAIProvider CreateOpenAIProviderWithNullApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new OpenAIProvider(httpClient, logger, null);
            }

            public static OpenAIProvider CreateOpenAIProviderWithValidApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new OpenAIProvider(httpClient, logger, "valid-openai-api-key");
            }

            public static GeminiProvider CreateGeminiProviderWithEmptyApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new GeminiProvider(httpClient, logger, "");
            }

            public static GeminiProvider CreateGeminiProviderWithNullApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new GeminiProvider(httpClient, logger, null);
            }

            public static GeminiProvider CreateGeminiProviderWithValidApiKey()
            {
                var logger = TestLogger.CreateNullLogger();
                var httpClient = new Mock<IHttpClient>().Object;
                return new GeminiProvider(httpClient, logger, "valid-gemini-api-key");
            }
        }

        #endregion

        #region ZaiGlmProvider Constructor Tests

        [Fact]
        public void TestZaiGlmProvider_Ctor_WithEmptyApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithEmptyApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestZaiGlmProvider_Ctor_WithNullApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithNullApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestZaiGlmProvider_Ctor_WithValidApiKey_Succeeds()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithValidApiKey();
            act.Should().NotThrow();
        }

        #endregion

        #region OpenAIProvider Constructor Tests

        [Fact]
        public void TestOpenAIProvider_Ctor_WithEmptyApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithEmptyApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestOpenAIProvider_Ctor_WithNullApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithNullApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestOpenAIProvider_Ctor_WithValidApiKey_Succeeds()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithValidApiKey();
            act.Should().NotThrow();
        }

        #endregion

        #region GeminiProvider Constructor Tests

        [Fact]
        public void TestGeminiProvider_Ctor_WithEmptyApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithEmptyApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestGeminiProvider_Ctor_WithNullApiKey_DoesNotThrow()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithNullApiKey();
            act.Should().NotThrow<ArgumentException>();
        }

        [Fact]
        public void TestGeminiProvider_Ctor_WithValidApiKey_Succeeds()
        {
            // Act & Assert
            Action act = () => BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithValidApiKey();
            act.Should().NotThrow();
        }

        #endregion

        #region ZaiGlmProvider IsConfigured Tests

        [Fact]
        public void TestZaiGlmProvider_IsConfigured_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithEmptyApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestZaiGlmProvider_IsConfigured_WithNullApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithNullApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestZaiGlmProvider_IsConfigured_WithWhitespaceApiKey_ReturnsFalse()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var httpClient = new Mock<IHttpClient>().Object;
            var provider = new ZaiGlmProvider(httpClient, logger, "   ");

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestZaiGlmProvider_IsConfigured_WithValidApiKey_ReturnsTrue()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithValidApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeTrue();
        }

        #endregion

        #region OpenAIProvider IsConfigured Tests

        [Fact]
        public void TestOpenAIProvider_IsConfigured_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithEmptyApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestOpenAIProvider_IsConfigured_WithNullApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithNullApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestOpenAIProvider_IsConfigured_WithWhitespaceApiKey_ReturnsFalse()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var httpClient = new Mock<IHttpClient>().Object;
            var provider = new OpenAIProvider(httpClient, logger, "   ");

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestOpenAIProvider_IsConfigured_WithValidApiKey_ReturnsTrue()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithValidApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeTrue();
        }

        #endregion

        #region GeminiProvider IsConfigured Tests

        [Fact]
        public void TestGeminiProvider_IsConfigured_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithEmptyApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestGeminiProvider_IsConfigured_WithNullApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithNullApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestGeminiProvider_IsConfigured_WithWhitespaceApiKey_ReturnsFalse()
        {
            // Arrange
            var logger = TestLogger.CreateNullLogger();
            var httpClient = new Mock<IHttpClient>().Object;
            var provider = new GeminiProvider(httpClient, logger, "   ");

            // Act & Assert
            provider.IsConfigured.Should().BeFalse();
        }

        [Fact]
        public void TestGeminiProvider_IsConfigured_WithValidApiKey_ReturnsTrue()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithValidApiKey();

            // Act & Assert
            provider.IsConfigured.Should().BeTrue();
        }

        #endregion

        #region GetRecommendationsAsync Tests

        [Fact]
        public async Task TestZaiGlmProvider_GetRecommendationsAsync_WithEmptyApiKey_ReturnsEmpty()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithEmptyApiKey();

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestOpenAIProvider_GetRecommendationsAsync_WithEmptyApiKey_ReturnsEmpty()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithEmptyApiKey();

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public async Task TestGeminiProvider_GetRecommendationsAsync_WithEmptyApiKey_ReturnsEmpty()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithEmptyApiKey();

            // Act
            var result = await provider.GetRecommendationsAsync("test prompt");

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        #endregion

        #region TestConnectionAsync Tests

        [Fact]
        public async Task TestZaiGlmProvider_TestConnectionAsync_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithEmptyApiKey();

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task TestOpenAIProvider_TestConnectionAsync_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithEmptyApiKey();

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task TestGeminiProvider_TestConnectionAsync_WithEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithEmptyApiKey();

            // Act
            var result = await provider.TestConnectionAsync();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region TestConnectionAsync with CancellationToken Tests

        [Fact]
        public async Task TestZaiGlmProvider_TestConnectionAsync_WithCancellationTokenAndEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateZaiGlmProviderWithEmptyApiKey();
            var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task TestOpenAIProvider_TestConnectionAsync_WithCancellationTokenAndEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateOpenAIProviderWithEmptyApiKey();
            var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task TestGeminiProvider_TestConnectionAsync_WithCancellationTokenAndEmptyApiKey_ReturnsFalse()
        {
            // Arrange
            var provider = BrainarrEmptyApiKeyTestFactory.CreateGeminiProviderWithEmptyApiKey();
            var cts = new CancellationTokenSource();

            // Act
            var result = await provider.TestConnectionAsync(cts.Token);

            // Assert
            result.Should().BeFalse();
        }

        #endregion
    }
}
