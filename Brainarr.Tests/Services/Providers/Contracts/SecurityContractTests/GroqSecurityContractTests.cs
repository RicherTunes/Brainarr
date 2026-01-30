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
    [Trait("Provider", "Groq")]
    [Collection("GroqSecurity")]
    public class GroqSecurityContractTests : IDisposable
    {
        private readonly List<string> _capturedLogs;
        private readonly Logger _logger;
        private readonly LogFactory _logFactory;

        public GroqSecurityContractTests()
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
            var apiKey = "gsk_test-secret-key-12345-production";
            var httpMock = new Mock<IHttpClient>();
            var provider = new GroqProvider(httpMock.Object, _logger, apiKey);
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("gsk_test-secret-key-12345-production");
        }

        [Fact]
        public async Task GetRecommendations_WithApiErrorContainingKey_DoesNotLogApiKey()
        {
            var apiKey = "gsk_secret-production-key";
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Invalid API key: gsk_secret-production-key\"}}",
                    HttpStatusCode.Unauthorized));

            var provider = new GroqProvider(httpMock.Object, _logger, apiKey);
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("gsk_secret-production-key");
        }

        [Fact]
        public async Task GetRecommendations_With429ContainingKey_DoesNotLogApiKey()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Rate limit exceeded for key gsk_leaked-key-abc\"}}",
                    HttpStatusCode.TooManyRequests));

            var provider = new GroqProvider(httpMock.Object, _logger, "test-key");
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("gsk_leaked-key-abc");
        }

        [Fact]
        public async Task GetRecommendations_WithServerError_DoesNotLogFullResponseBody()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(HttpResponseFactory.CreateResponse(
                    "{\"error\": {\"message\": \"Internal error with key gsk_internal-secret\"}}",
                    HttpStatusCode.InternalServerError));

            var provider = new GroqProvider(httpMock.Object, _logger, "test-key");
            await provider.GetRecommendationsAsync("Test prompt");

            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("gsk_internal-secret");
        }

        [Fact]
        public async Task TestConnection_WithException_DoesNotCrash()
        {
            var httpMock = new Mock<IHttpClient>();
            httpMock.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new Exception("Unexpected error"));

            var provider = new GroqProvider(httpMock.Object, _logger, "test-key");
            var result = await provider.TestConnectionAsync();
            result.Should().BeFalse();
        }

        [Fact]
        public void UpdateModel_DoesNotLogSensitiveInfo()
        {
            var httpMock = new Mock<IHttpClient>();
            var provider = new GroqProvider(httpMock.Object, _logger, "gsk_test-key");
            provider.UpdateModel("llama-3.1-70b-versatile");
            var allLogs = string.Join("\n", _capturedLogs);
            allLogs.Should().NotContain("gsk_test-key");
        }
    }
}
