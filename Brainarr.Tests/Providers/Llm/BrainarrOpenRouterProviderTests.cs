using System;
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
    /// Unit coverage for <see cref="BrainarrOpenRouterProvider"/>.
    /// </summary>
    public class BrainarrOpenRouterProviderTests
    {
        private readonly Mock<IHttpClient> _http;
        private readonly Logger _logger;

        public BrainarrOpenRouterProviderTests()
        {
            _http = new Mock<IHttpClient>();
            _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Capabilities_ReportsExpectedFlags()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "anthropic/claude-3.5-sonnet");

            provider.ProviderId.Should().Be("openrouter");
            provider.DisplayName.Should().Be("OpenRouter");
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.TextCompletion).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.Streaming).Should().BeTrue();
        }

        [Fact]
        public void Constructor_NullHttpClient_Throws()
        {
            Action act = () => new BrainarrOpenRouterProvider(null!, _logger, "sk-or-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => new BrainarrOpenRouterProvider(_http.Object, null!, "sk-or-test");
            act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
        }

        [Fact]
        public void Constructor_EmptyApiKey_Throws()
        {
            Action act = () => new BrainarrOpenRouterProvider(_http.Object, _logger, "");
            act.Should().Throw<ArgumentException>().WithParameterName("apiKey");
        }

        [Fact]
        public async Task CompleteAsync_HappyPath_ReturnsContentAndUsage()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            var apiObj = new
            {
                id = "gen-1",
                model = "openai/gpt-4o-mini",
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        message = new { role = "assistant", content = "{\"recommendations\":[]}" },
                        finish_reason = "stop",
                    },
                },
                usage = new { prompt_tokens = 12, completion_tokens = 7, total_tokens = 19 },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "hello" }, CancellationToken.None);

            response.Should().NotBeNull();
            response.Content.Should().Be("{\"recommendations\":[]}");
            response.FinishReason.Should().Be("stop");
            response.Usage.Should().NotBeNull();
            response.Usage!.InputTokens.Should().Be(12);
            response.Usage!.OutputTokens.Should().Be(7);
        }

        [Fact]
        public async Task CompleteAsync_RoutedModel_SurfacesViaMetadata()
        {
            // OpenRouter often returns the actual upstream model that handled the request
            // (e.g. when 'openrouter/auto' picks one). Provider surfaces it under metadata
            // for observability.
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openrouter/auto");
            var apiObj = new
            {
                model = "anthropic/claude-3.5-sonnet",
                choices = new[]
                {
                    new
                    {
                        message = new { content = "[]" },
                        finish_reason = "stop",
                    },
                },
            };
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(Newtonsoft.Json.JsonConvert.SerializeObject(apiObj)));

            var response = await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            response.Metadata.Should().NotBeNull();
            response.Metadata!["routed_model"].Should().Be("anthropic/claude-3.5-sonnet");
        }

        [Fact]
        public async Task SendAsync_AttachesHttpRefererAndXTitleHeaders()
        {
            // OpenRouter uses these headers to attribute requests on its dashboard.
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");

            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(r => captured = r)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            await provider.CompleteAsync(new LlmRequest { Prompt = "x" });

            captured.Should().NotBeNull();
            // Headers are expected to be present; we can't easily inspect HttpHeader values
            // (read-only after construction in some host versions), but verify auth header reaches.
            var auth = captured!.Headers.GetSingleValue("Authorization");
            auth.Should().StartWith("Bearer ");
            captured.Headers.GetSingleValue("HTTP-Referer").Should().Contain("github.com/RicherTunes/Brainarr");
            captured.Headers.GetSingleValue("X-Title").Should().Be("Brainarr");
        }

        [Fact]
        public async Task CompleteAsync_401_ThrowsAuthenticationException()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<AuthenticationException>();
            ex.Which.ProviderId.Should().Be("openrouter");
        }

        [Fact]
        public async Task CompleteAsync_429_ThrowsRateLimitException()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            await act.Should().ThrowAsync<RateLimitException>();
        }

        [Fact]
        public async Task CompleteAsync_503_ThrowsProviderOverloaded()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.ServiceUnavailable));

            Func<Task> act = async () => await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            var ex = await act.Should().ThrowAsync<ProviderException>();
            ex.Which.ErrorCode.Should().Be(LlmErrorCode.ProviderOverloaded);
        }

        [Fact]
        public async Task CheckHealthAsync_Ok_IsHealthy()
        {
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}"));

            var health = await provider.CheckHealthAsync();

            health.IsHealthy.Should().BeTrue();
            health.Provider.Should().Be("openrouter");
            health.AuthMethod.Should().Be("apiKey");
        }

        [Fact]
        public void StreamAsync_NowReturnsNonNullEnumerable()
        {
            // Tech debt wave 2: StreamingHttpExecutor bridge wired.
            var provider = new BrainarrOpenRouterProvider(_http.Object, _logger, "sk-or-test", "openai/gpt-4o-mini");
            var stream = provider.StreamAsync(new LlmRequest { Prompt = "x" });
            stream.Should().NotBeNull();
        }
    }
}
