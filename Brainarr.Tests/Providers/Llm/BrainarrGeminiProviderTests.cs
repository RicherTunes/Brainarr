using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Unit coverage for <see cref="BrainarrGeminiProvider"/>.
    /// </summary>
    public class BrainarrGeminiProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrGeminiProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");

            provider.ProviderId.Should().Be("gemini");
            provider.DisplayName.Should().Be("Google Gemini");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeFalse();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrGeminiProvider(null!, _logger, "AIza-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrGeminiProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContent()
        {
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");
            var apiObj = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new[] { new { text = "{\"recommendations\":[]}" } },
                        },
                        finishReason = "STOP",
                    },
                },
                usageMetadata = new { promptTokenCount = 8, candidatesTokenCount = 5, totalTokenCount = 13 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("STOP");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(8);
            response.Usage!.OutputTokens.Should().Be(5);
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("gemini");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_403_ServiceDisabled_CapturesActivationUrl()
        {
            // Gemini's signature error: PERMISSION_DENIED with an activationUrl in details.metadata.
            // The provider should hand a hint with the activation URL through to the user via
            // IBrainarrLlmHintSource.
            var errorBody = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                error = new
                {
                    code = 403,
                    status = "PERMISSION_DENIED",
                    message = "Generative Language API has not been used in project 12345",
                    details = new[]
                    {
                        new
                        {
                            metadata = new
                            {
                                activationUrl = "https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview?project=12345",
                                consumer = "projects/12345",
                            },
                        },
                    },
                },
            });
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Forbidden, errorBody));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>(); // 403 → AuthorizationFailed sub-type
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.AuthorizationFailed);

            var hintSource = (IBrainarrLlmHintSource)provider;
            var hint = hintSource.GetUserHint(ex.Which);
            hint.Should().NotBeNull();
            hint!.Message.Should().Contain("Enable the Generative Language API");
            hint.Message.Should().Contain("project=12345");
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrGeminiProvider(_http.Object, _logger, "AIza-test", "gemini-1.5-flash");
            var apiObj = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text = "OK" } } },
                        finishReason = "STOP",
                    },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("gemini");
        }
    }
}
