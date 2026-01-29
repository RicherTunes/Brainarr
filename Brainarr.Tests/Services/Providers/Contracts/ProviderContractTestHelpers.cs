using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Providers.Contracts
{
    /// <summary>
    /// Reusable test helpers for provider contract tests.
    /// All HTTP-based providers should pass these standard scenarios.
    /// </summary>
    public static class ProviderContractTestHelpers
    {
        public static Logger CreateTestLogger() => TestLogger.CreateNullLogger();

        #region Mock HTTP Responses

        /// <summary>
        /// Creates a mock HTTP client that returns a timeout exception.
        /// </summary>
        public static Mock<IHttpClient> CreateTimeoutMock()
        {
            var mock = new Mock<IHttpClient>();
            mock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
            return mock;
        }

        /// <summary>
        /// Creates a mock HTTP client that returns a 429 Too Many Requests response.
        /// </summary>
        public static Mock<IHttpClient> CreateRateLimitMock(string? body = null)
        {
            return CreateStatusCodeMock(HttpStatusCode.TooManyRequests, body ?? "{\"error\": {\"message\": \"Rate limit exceeded\"}}");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns a 401 Unauthorized response.
        /// </summary>
        public static Mock<IHttpClient> CreateUnauthorizedMock(string? body = null)
        {
            return CreateStatusCodeMock(HttpStatusCode.Unauthorized, body ?? "{\"error\": {\"message\": \"Invalid API key\"}}");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns a 500 Internal Server Error.
        /// </summary>
        public static Mock<IHttpClient> CreateServerErrorMock(string? body = null)
        {
            return CreateStatusCodeMock(HttpStatusCode.InternalServerError, body ?? "{\"error\": {\"message\": \"Internal server error\"}}");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns malformed JSON.
        /// </summary>
        public static Mock<IHttpClient> CreateMalformedJsonMock()
        {
            return CreateStatusCodeMock(HttpStatusCode.OK, "{ this is not valid JSON [");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns an empty response body.
        /// </summary>
        public static Mock<IHttpClient> CreateEmptyResponseMock()
        {
            return CreateStatusCodeMock(HttpStatusCode.OK, "");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns valid JSON but unexpected schema.
        /// </summary>
        public static Mock<IHttpClient> CreateUnexpectedSchemaMock()
        {
            return CreateStatusCodeMock(HttpStatusCode.OK, "{\"unexpected\": \"schema\", \"no_choices\": true}");
        }

        /// <summary>
        /// Creates a mock HTTP client that returns null content in choices.
        /// </summary>
        public static Mock<IHttpClient> CreateNullContentMock()
        {
            return CreateStatusCodeMock(HttpStatusCode.OK,
                "{\"choices\": [{\"message\": {\"content\": null}, \"finish_reason\": \"stop\"}]}");
        }

        /// <summary>
        /// Creates a mock HTTP client that throws a cancellation exception.
        /// </summary>
        public static Mock<IHttpClient> CreateCancellationMock()
        {
            var mock = new Mock<IHttpClient>();
            mock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new OperationCanceledException("The operation was canceled."));
            return mock;
        }

        /// <summary>
        /// Creates a mock HTTP client that throws a network error.
        /// </summary>
        public static Mock<IHttpClient> CreateNetworkErrorMock()
        {
            var mock = new Mock<IHttpClient>();
            mock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("Unable to connect to the remote server"));
            return mock;
        }

        /// <summary>
        /// Creates a mock HTTP client that returns a specific status code.
        /// </summary>
        public static Mock<IHttpClient> CreateStatusCodeMock(HttpStatusCode statusCode, string content)
        {
            var mock = new Mock<IHttpClient>();
            var response = CreateMockResponse(statusCode, content);
            mock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(response);
            return mock;
        }

        /// <summary>
        /// Creates a mock HTTP client that returns valid recommendations.
        /// </summary>
        public static Mock<IHttpClient> CreateSuccessfulResponseMock(string providerFormat, int count = 3)
        {
            var content = GenerateSuccessfulResponse(providerFormat, count);
            return CreateStatusCodeMock(HttpStatusCode.OK, content);
        }

        #endregion

        #region Response Generation

        /// <summary>
        /// Generate a successful response in the specified provider format.
        /// </summary>
        public static string GenerateSuccessfulResponse(string providerFormat, int count)
        {
            var recommendations = GenerateRecommendationsJson(count);

            return providerFormat.ToLowerInvariant() switch
            {
                "openai" or "groq" or "deepseek" or "zaiglm" =>
                    $"{{\"id\":\"test\",\"choices\":[{{\"message\":{{\"content\":\"{EscapeJson(recommendations)}\"}},\"finish_reason\":\"stop\"}}],\"usage\":{{\"prompt_tokens\":100,\"completion_tokens\":50,\"total_tokens\":150}}}}",

                "anthropic" =>
                    $"{{\"id\":\"test\",\"type\":\"message\",\"content\":[{{\"type\":\"text\",\"text\":\"{EscapeJson(recommendations)}\"}}],\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":100,\"output_tokens\":50}}}}",

                "gemini" =>
                    $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{EscapeJson(recommendations)}\"}}]}},\"finishReason\":\"STOP\"}}],\"usageMetadata\":{{\"promptTokenCount\":100,\"candidatesTokenCount\":50}}}}",

                _ => recommendations
            };
        }

        private static string GenerateRecommendationsJson(int count)
        {
            var items = new List<string>();
            var artists = new[] { "Radiohead", "Arcade Fire", "Bon Iver", "Fleet Foxes", "Sufjan Stevens" };
            var albums = new[] { "OK Computer", "The Suburbs", "For Emma", "Helplessness Blues", "Illinois" };
            var genres = new[] { "Alternative Rock", "Indie Rock", "Folk", "Chamber Pop", "Baroque Pop" };

            for (int i = 0; i < count && i < artists.Length; i++)
            {
                items.Add($"{{\\\"artist\\\":\\\"{artists[i]}\\\",\\\"album\\\":\\\"{albums[i]}\\\",\\\"genre\\\":\\\"{genres[i]}\\\",\\\"confidence\\\":0.9,\\\"year\\\":2007,\\\"reason\\\":\\\"Test recommendation\\\"}}");
            }

            return "{\\\"recommendations\\\":[" + string.Join(",", items) + "]}";
        }

        private static string EscapeJson(string json)
        {
            return json.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        private static HttpResponse CreateMockResponse(HttpStatusCode statusCode, string content)
        {
            // Use reflection or a test helper to create HttpResponse
            // This is a simplified version - actual implementation depends on NzbDrone.Common.Http internals
            var response = new HttpResponse(
                new HttpRequest($"https://api.test.com/v1/chat/completions"),
                new HttpHeader(),
                content,
                statusCode);
            return response;
        }

        #endregion

        #region Assertion Helpers

        /// <summary>
        /// Assert that the provider returns empty list for the given mock.
        /// </summary>
        public static async Task AssertReturnsEmptyOnError<TProvider>(
            Func<Mock<IHttpClient>, Logger, TProvider> providerFactory,
            Mock<IHttpClient> httpMock)
            where TProvider : IAIProvider
        {
            var logger = CreateTestLogger();
            var provider = providerFactory(httpMock, logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            if (result.Count != 0)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected empty list but got {result.Count} recommendations");
            }
        }

        /// <summary>
        /// Assert that TestConnection returns false for the given mock.
        /// </summary>
        public static async Task AssertTestConnectionFails<TProvider>(
            Func<Mock<IHttpClient>, Logger, TProvider> providerFactory,
            Mock<IHttpClient> httpMock)
            where TProvider : IAIProvider
        {
            var logger = CreateTestLogger();
            var provider = providerFactory(httpMock, logger);

            var result = await provider.TestConnectionAsync();

            if (result)
            {
                throw new Xunit.Sdk.XunitException(
                    "Expected TestConnection to return false but got true");
            }
        }

        /// <summary>
        /// Assert that the provider successfully parses recommendations.
        /// </summary>
        public static async Task AssertParsesRecommendations<TProvider>(
            Func<Mock<IHttpClient>, Logger, TProvider> providerFactory,
            Mock<IHttpClient> httpMock,
            int expectedCount)
            where TProvider : IAIProvider
        {
            var logger = CreateTestLogger();
            var provider = providerFactory(httpMock, logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            if (result.Count != expectedCount)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Expected {expectedCount} recommendations but got {result.Count}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Base test class for provider contract tests.
    /// Inherit from this and implement CreateProvider to get standard test coverage.
    /// </summary>
    public abstract class ProviderContractTestBase<TProvider> where TProvider : IAIProvider
    {
        protected readonly Logger Logger = ProviderContractTestHelpers.CreateTestLogger();

        /// <summary>
        /// Create the provider instance with the given HTTP mock.
        /// </summary>
        protected abstract TProvider CreateProvider(Mock<IHttpClient> httpMock, Logger logger);

        /// <summary>
        /// The provider format string for generating responses (e.g., "openai", "anthropic").
        /// </summary>
        protected abstract string ProviderFormat { get; }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithTimeout_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateTimeoutMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithCancellation_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateCancellationMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_With429RateLimit_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateRateLimitMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_With401Unauthorized_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateUnauthorizedMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_With500ServerError_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateServerErrorMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithMalformedJson_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateMalformedJsonMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithEmptyResponse_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateEmptyResponseMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithUnexpectedSchema_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateUnexpectedSchemaMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithNullContent_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateNullContentMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithNetworkError_ReturnsEmptyList()
        {
            var httpMock = ProviderContractTestHelpers.CreateNetworkErrorMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.Empty(result);
        }

        [Xunit.Fact]
        public virtual async Task TestConnection_With401_ReturnsFalse()
        {
            var httpMock = ProviderContractTestHelpers.CreateUnauthorizedMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.TestConnectionAsync();

            Xunit.Assert.False(result);
        }

        [Xunit.Fact]
        public virtual async Task TestConnection_WithTimeout_ReturnsFalse()
        {
            var httpMock = ProviderContractTestHelpers.CreateTimeoutMock();
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.TestConnectionAsync();

            Xunit.Assert.False(result);
        }

        [Xunit.Fact]
        public virtual async Task GetRecommendations_WithValidResponse_ParsesRecommendations()
        {
            var httpMock = ProviderContractTestHelpers.CreateSuccessfulResponseMock(ProviderFormat, 3);
            var provider = CreateProvider(httpMock, Logger);

            var result = await provider.GetRecommendationsAsync("Test prompt");

            Xunit.Assert.NotEmpty(result);
            Xunit.Assert.True(result.Count >= 1, $"Expected at least 1 recommendation but got {result.Count}");
        }
    }
}
