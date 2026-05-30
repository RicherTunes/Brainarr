using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Unit coverage for <see cref="BrainarrPerplexityProvider"/>.
    /// </summary>
    public class BrainarrPerplexityProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrPerplexityProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public async Task CompleteAsync_UsesBearerAuth_AgainstPerplexityEndpoint()
        {
            // Contract guard (MED-1, provider-matrix sweep): pin auth header + endpoint on CompleteAsync.
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-xyz", "sonar-pro");
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"[]\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured.Should().NotBeNull();
            captured!.Headers.GetSingleValue("Authorization").Should().Be("Bearer pplx-xyz");
            captured.Url.ToString().Should().Be("https://api.perplexity.ai/chat/completions");
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");

            provider.ProviderId.Should().Be("perplexity");
            provider.DisplayName.Should().Be("Perplexity");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt).Should().BeTrue();
            // JsonMode intentionally omitted: gated per-route. See provider doc-comment.
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeFalse();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrPerplexityProvider(null!, _logger, "pplx-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrPerplexityProvider(_http.Object, null!, "pplx-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrPerplexityProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "{\"recommendations\":[]}" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 8, completion_tokens = 4, total_tokens = 12 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage!.InputTokens.Should().Be(8);
            response.Usage!.OutputTokens.Should().Be(4);
        }

        [Fact]
        public async Task CompleteAsync_TopLevelCitations_SurfacedViaMetadata()
        {
            // Older Sonar shape: citations at top of payload.
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            var apiObj = new
            {
                choices = new[]
                {
                    new { message = new { content = "Some answer." }, finish_reason = "stop" },
                },
                citations = new[] { "https://en.wikipedia.org/wiki/Test", "https://example.com/source" },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Metadata.Should().NotBeNull();
            response.Metadata!.Should().ContainKey("citations");
            var citations = (List<string>)response.Metadata!["citations"];
            citations.Should().HaveCount(2);
            citations.Should().Contain("https://en.wikipedia.org/wiki/Test");
            citations.Should().Contain("https://example.com/source");
        }

        [Fact]
        public async Task CompleteAsync_PerChoiceCitations_SurfacedViaMetadata()
        {
            // Newer Sonar shape: citations on the choice. Provider must merge and dedupe.
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            var apiObj = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new { content = "Some answer." },
                        finish_reason = "stop",
                        citations = new[] { "https://a.example", "https://b.example" },
                    },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Metadata.Should().NotBeNull();
            var citations = (List<string>)response.Metadata!["citations"];
            citations.Should().BeEquivalentTo(new[] { "https://a.example", "https://b.example" });
        }

        [Fact]
        public async Task CompleteAsync_StripsInlineCitationMarkers()
        {
            // Perplexity Sonar often inserts [1], [12] markers inline; provider strips them
            // defensively before returning content.
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            var apiObj = new
            {
                choices = new[]
                {
                    new { message = new { content = "Pink Floyd[1] is a band[12]." }, finish_reason = "stop" },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Content.Should().Be("Pink Floyd is a band.");
        }

        [Fact]
        public async Task CompleteAsync_NoCitations_MetadataIsNull()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            var apiObj = new
            {
                choices = new[] { new { message = new { content = "[]" }, finish_reason = "stop" } },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Metadata.Should().BeNull();
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("perplexity");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_503_ThrowsProviderOverloaded()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrPerplexityProvider(_http.Object, _logger, "pplx-test", "sonar-pro");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("perplexity");
            health.AuthMethod.Should().Be("apiKey");
        }
    }
}
