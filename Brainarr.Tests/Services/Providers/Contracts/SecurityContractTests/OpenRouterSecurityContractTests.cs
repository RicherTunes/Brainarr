using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Providers.Contracts.SecurityContractTests
{
    [Trait("Category", "Security")]
    [Trait("Provider", "OpenRouter")]
    [Collection("OpenRouterSecurity")]
    public class OpenRouterSecurityContractTests : IDisposable
    {
        private readonly List<string> _capturedLogs;
        private readonly Logger _logger;
        private readonly LogFactory _logFactory;

        public OpenRouterSecurityContractTests()
        {
            _capturedLogs = new List<string>();
            var config = new LoggingConfiguration();
            var target = new MethodCallTarget("testcapture", (logEvent, parameters) =>
            {
                _capturedLogs.Add(logEvent.FormattedMessage);
            });
            config.AddTarget(target);
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, target);
            _logFactory = new LogFactory(config);
            _logger = _logFactory.GetCurrentClassLogger();
        }

        public void Dispose() => _logFactory.Shutdown();

        [Fact]
        public void Constructor_WithValidApiKey_DoesNotLogApiKey()
        {
            var apiKey = "sk-or-v1-test-secret-key-12345";
            var httpMock = new Mock<IHttpClient>();
            var provider = new OpenRouterProvider(httpMock.Object, _logger, apiKey);
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-test-secret-key-12345");
        }

        [Fact]
        public async Task GetRecommendations_WithApiErrorContainingKey_DoesNotLogApiKey()
        {
            var apiKey = "sk-or-v1-secret-production-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Invalid API key: sk-or-v1-secret-production-key\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new OpenRouterProvider(httpMock.Object, _logger, apiKey);
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-secret-production-key");
        }

        [Fact]
        public async Task GetRecommendations_With429ContainingKey_DoesNotLogApiKey()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Rate limit exceeded for key sk-or-v1-leaked-key\"}}",
                    HttpStatusCode.TooManyRequests));

            var provider = new OpenRouterProvider(httpMock.Object, _logger, "test-key");
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-leaked-key");
        }

        [Fact]
        public async Task GetRecommendations_WithServerError_DoesNotLogFullResponseBody()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Internal error with key sk-or-v1-internal-secret\"}}",
                    HttpStatusCode.InternalServerError));

            var provider = new OpenRouterProvider(httpMock.Object, _logger, "test-key");
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-internal-secret");
        }

        [Fact]
        public async Task TestConnection_WithException_DoesNotCrash()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            var provider = new OpenRouterProvider(httpMock.Object, _logger, "test-key");
            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse();
        }

        [Fact]
        public async Task GetRecommendations_WithInnerExceptionContainingApiKey_DoesNotLogApiKey()
        {
            var innerException = new Exception("Connection failed with key sk-or-v1-inner-secret-leaked-key");
            var outerException = new Exception("HTTP request failed", innerException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new OpenRouterProvider(httpMock.Object, _logger, "sk-or-v1-test-key");
            var result = await provider.GetRecommendationsAsync("Test prompt");

            result.Should().BeEmpty();
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-inner-secret-leaked-key");
        }

        [Fact]
        public async Task GetRecommendations_WithNestedInnerExceptions_DoesNotLogApiKey()
        {
            var deepestException = new Exception("Auth failed: api_key=sk-or-v1-deepest-secret-789");
            var middleException = new Exception("Request error with sk-or-v1-middle-secret-456", deepestException);
            var outerException = new Exception("HTTP client error", middleException);

            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(outerException);

            var provider = new OpenRouterProvider(httpMock.Object, _logger, "sk-or-v1-test-key");
            var result = await provider.GetRecommendationsAsync("Test prompt");

            result.Should().BeEmpty();
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-deepest-secret-789");
            allLogs.Should().NotContain("sk-or-v1-middle-secret-456");
        }

        [Fact]
        public void UpdateModel_DoesNotLogSensitiveInfo()
        {
            var httpMock = new Mock<IHttpClient>();
            var provider = new OpenRouterProvider(httpMock.Object, _logger, "sk-or-v1-test-key");
            provider.UpdateModel("anthropic/claude-3-5-sonnet");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("sk-or-v1-test-key");
        }
    }
}
