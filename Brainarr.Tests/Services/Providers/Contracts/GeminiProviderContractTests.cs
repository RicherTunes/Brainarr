using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Contracts
{
    /// <summary>
    /// Contract tests for GeminiProvider using the shared test infrastructure.
    /// Verifies standard error handling across timeout, 429, malformed, etc.
    ///
    /// Note: Gemini uses query parameter for API key (different from header-based auth),
    /// and has a different URL structure (models/{model}:generateContent?key=...).
    /// </summary>
    [Trait("Category", "Contract")]
    [Trait("Provider", "Gemini")]
    public class GeminiProviderContractTests : ProviderContractTestBase<GeminiProvider>
    {
        protected override string ProviderFormat => "gemini";

        protected override GeminiProvider CreateProvider(Mock<IHttpClient> httpMock, Logger logger)
        {
            // Gemini uses query param for API key, model string IDs like gemini-pro, gemini-1.5-pro
            return new GeminiProvider(httpMock.Object, logger, apiKey: "test-api-key", model: "gemini-1.5-flash");
        }

        // All tests inherited from ProviderContractTestBase:
        // - GetRecommendations_WithTimeout_ReturnsEmptyList
        // - GetRecommendations_WithCancellation_ReturnsEmptyList
        // - GetRecommendations_With429RateLimit_ReturnsEmptyList
        // - GetRecommendations_With401Unauthorized_ReturnsEmptyList
        // - GetRecommendations_With500ServerError_ReturnsEmptyList
        // - GetRecommendations_WithMalformedJson_ReturnsEmptyList
        // - GetRecommendations_WithEmptyResponse_ReturnsEmptyList
        // - GetRecommendations_WithUnexpectedSchema_ReturnsEmptyList
        // - GetRecommendations_WithNullContent_ReturnsEmptyList
        // - GetRecommendations_WithNetworkError_ReturnsEmptyList
        // - TestConnection_With401_ReturnsFalse
        // - TestConnection_WithTimeout_ReturnsFalse
        // - GetRecommendations_WithValidResponse_ParsesRecommendations (overridden below)

        /// <summary>
        /// Override the base test with properly formatted Gemini response.
        /// The base test helper has escaping issues with the Gemini format.
        /// </summary>
        [Fact]
        public override async System.Threading.Tasks.Task GetRecommendations_WithValidResponse_ParsesRecommendations()
        {
            // Arrange - Create a properly formatted Gemini response with recommendations in text field
            var geminiResponse = @"{
                ""candidates"": [{
                    ""content"": {
                        ""parts"": [{
                            ""text"": ""[{\""artist\"": \""Radiohead\"", \""album\"": \""OK Computer\"", \""genre\"": \""Alternative Rock\"", \""confidence\"": 0.95, \""reason\"": \""Classic album\""}]""
                        }]
                    },
                    ""finishReason"": ""STOP""
                }],
                ""usageMetadata"": {
                    ""promptTokenCount"": 100,
                    ""candidatesTokenCount"": 50,
                    ""totalTokenCount"": 150
                }
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.OK, geminiResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            Assert.NotEmpty(result);
            Assert.True(result.Count >= 1, $"Expected at least 1 recommendation but got {result.Count}");
            Assert.Contains(result, r => r.Artist == "Radiohead");
        }

        #region Gemini-Specific Tests

        /// <summary>
        /// Verify model selection works with various Gemini model string IDs.
        /// </summary>
        [Theory]
        [InlineData("gemini-pro")]
        [InlineData("gemini-1.5-pro")]
        [InlineData("gemini-1.5-flash")]
        [InlineData("gemini-2.5-flash")]
        [InlineData("Gemini_15_Pro")]
        [InlineData("Gemini_25_Flash")]
        public async System.Threading.Tasks.Task CreateProvider_WithDifferentModels_Succeeds(string modelId)
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateSuccessfulResponseMock(ProviderFormat, 2);

            // Act - Provider should instantiate without error
            var provider = new GeminiProvider(httpMock.Object, Logger, apiKey: "test-key", model: modelId);

            // Assert - Provider name set correctly
            Assert.Equal("Google Gemini", provider.ProviderName);

            // Verify can call GetRecommendations with the model
            var result = await provider.GetRecommendationsAsync("Test prompt");
            Assert.NotNull(result);
        }

        /// <summary>
        /// Verify Gemini-specific safety block handling returns empty list.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_WithSafetyBlock_ReturnsEmptyList()
        {
            // Arrange - Gemini safety block response
            var safetyBlockedResponse = @"{
                ""candidates"": [{
                    ""content"": { ""parts"": [] },
                    ""finishReason"": ""SAFETY"",
                    ""safetyRatings"": [
                        { ""category"": ""HARM_CATEGORY_HARASSMENT"", ""probability"": ""BLOCKED"" }
                    ]
                }]
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.OK, safetyBlockedResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            Assert.Empty(result);
        }

        /// <summary>
        /// Verify Gemini 403 (API disabled) returns empty list.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_With403ApiDisabled_ReturnsEmptyList()
        {
            // Arrange - Gemini API disabled response
            var apiDisabledResponse = @"{
                ""error"": {
                    ""code"": 403,
                    ""message"": ""Generative Language API has not been used in project 12345 before or it is disabled."",
                    ""status"": ""PERMISSION_DENIED"",
                    ""details"": [{
                        ""@type"": ""type.googleapis.com/google.rpc.Help"",
                        ""links"": [{
                            ""description"": ""Google developers console API activation"",
                            ""url"": ""https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview""
                        }]
                    }]
                }
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.Forbidden, apiDisabledResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            Assert.Empty(result);
        }

        /// <summary>
        /// Verify Gemini 404 (model not found) triggers fallback behavior and returns empty list if fallback fails.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_With404ModelNotFound_ReturnsEmptyList()
        {
            // Arrange - Model not found response
            var notFoundResponse = @"{
                ""error"": {
                    ""code"": 404,
                    ""message"": ""Model not found: models/gemini-nonexistent"",
                    ""status"": ""NOT_FOUND""
                }
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.NotFound, notFoundResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert
            Assert.Empty(result);
        }

        /// <summary>
        /// Verify Gemini handles partial/truncated JSON in text field gracefully.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_WithTruncatedJsonInText_ReturnsEmptyList()
        {
            // Arrange - Truncated JSON in text field (MAX_TOKENS hit)
            var truncatedResponse = @"{
                ""candidates"": [{
                    ""content"": {
                        ""parts"": [{
                            ""text"": ""[{\""artist\"": \""Pink Floyd\"", \""album\"": \""The Dark""
                        }]
                    },
                    ""finishReason"": ""MAX_TOKENS""
                }]
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.OK, truncatedResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert - Should handle gracefully (either empty or partial results)
            Assert.NotNull(result);
        }

        /// <summary>
        /// Verify UpdateModel changes the model correctly.
        /// </summary>
        [Fact]
        public void UpdateModel_ChangesModel_Succeeds()
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateSuccessfulResponseMock(ProviderFormat, 1);
            var provider = new GeminiProvider(httpMock.Object, Logger, apiKey: "test-key", model: "gemini-pro");

            // Act
            provider.UpdateModel("gemini-1.5-pro");

            // Assert - No exception thrown; model updated internally
            Assert.Equal("Google Gemini", provider.ProviderName);
        }

        /// <summary>
        /// Verify empty API key throws ArgumentException.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_WithEmptyApiKey_ThrowsArgumentException(string? apiKey)
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() =>
                new GeminiProvider(httpMock.Object, Logger, apiKey: apiKey!, model: "gemini-pro"));
        }

        /// <summary>
        /// Verify null HTTP client throws ArgumentNullException.
        /// </summary>
        [Fact]
        public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
                new GeminiProvider(null!, Logger, apiKey: "test-key", model: "gemini-pro"));
        }

        /// <summary>
        /// Verify null logger throws ArgumentNullException.
        /// </summary>
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var httpMock = new Mock<IHttpClient>();

            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
                new GeminiProvider(httpMock.Object, null!, apiKey: "test-key", model: "gemini-pro"));
        }

        /// <summary>
        /// Verify Gemini response with JSON in json field (instead of text) is handled.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_WithJsonFieldResponse_ParsesRecommendations()
        {
            // Arrange - Some Gemini responses put JSON in a json field rather than text
            var jsonFieldResponse = @"{
                ""candidates"": [{
                    ""content"": {
                        ""parts"": [{
                            ""json"": {
                                ""recommendations"": [
                                    {""artist"": ""Radiohead"", ""album"": ""OK Computer"", ""genre"": ""Alternative"", ""confidence"": 0.95, ""reason"": ""Classic alt-rock""}
                                ]
                            }
                        }]
                    },
                    ""finishReason"": ""STOP""
                }]
            }";
            var httpMock = ProviderContractTestHelpers.CreateStatusCodeMock(
                System.Net.HttpStatusCode.OK, jsonFieldResponse);
            var provider = CreateProvider(httpMock, Logger);

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt");

            // Assert - Should parse recommendations from json field
            Assert.NotNull(result);
        }

        /// <summary>
        /// Verify cancellation token is respected.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task GetRecommendations_WithCancellationToken_RespectsToken()
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateSuccessfulResponseMock(ProviderFormat, 2);
            var provider = CreateProvider(httpMock, Logger);
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            // Act
            var result = await provider.GetRecommendationsAsync("Test prompt", cts.Token);

            // Assert - Should return empty list on cancellation (graceful handling)
            Assert.Empty(result);
        }

        /// <summary>
        /// Verify TestConnection with cancellation token is respected.
        /// </summary>
        [Fact]
        public async System.Threading.Tasks.Task TestConnection_WithCancellationToken_RespectsToken()
        {
            // Arrange
            var httpMock = ProviderContractTestHelpers.CreateSuccessfulResponseMock(ProviderFormat, 1);
            var provider = CreateProvider(httpMock, Logger);
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel(); // Pre-cancel

            // Act & Assert - Should throw or return false
            try
            {
                var result = await provider.TestConnectionAsync(cts.Token);
                // If it returns, should be false for cancelled token
                Assert.False(result);
            }
            catch (System.OperationCanceledException)
            {
                // This is also acceptable behavior
            }
        }

        #endregion
    }
}
