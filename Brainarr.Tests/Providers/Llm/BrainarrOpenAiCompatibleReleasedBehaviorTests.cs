using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Characterizes the released, publicly constructible OpenAI-compatible adapter before
    /// any shared-base adoption. These tests deliberately describe current behavior; policy
    /// changes require a separate compatibility decision.
    /// </summary>
    public sealed class BrainarrOpenAiCompatibleReleasedBehaviorTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();

        [Fact]
        public async Task CompleteAsync_FloatTemperatureAndEscaping_MatchReleasedNewtonsoftBody()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            var provider = CreateProvider();

            await provider.CompleteAsync(new LlmRequest
            {
                Prompt = "astral 🎵 <tag>\ncontrol\t",
                Temperature = 0.2f,
            });

            var body = Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>());
            body.Should().Contain("\"temperature\":0.20000000298023224");
            body.Should().Contain("🎵 <tag>\\ncontrol\\t");
            body.Should().NotContain("\\uD83C\\uDFB5");
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("\uFEFF{\"choices\":[{\"message\":{\"content\":\"bom\"}}]}", "bom")]
        [InlineData("{not-json", "{not-json")]
        public async Task CompleteAsync_BlankBomAndMalformedPayloads_PreserveReleasedParsing(
            string payload,
            string expected)
        {
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(payload));

            var response = await CreateProvider().CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be(expected);
        }

        [Fact]
        public async Task CompleteAsync_RequestTimeout_IsIgnoredByReleasedAdapter()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await CreateProvider().CompleteAsync(new LlmRequest
            {
                Prompt = "hi",
                Timeout = TimeSpan.FromMilliseconds(1),
            });

            captured!.RequestTimeout.Should().NotBe(TimeSpan.FromMilliseconds(1));
        }

        [Fact]
        public async Task CompleteAsync_OpenAuthCircuit_PrecedesCallerCancellation()
        {
            const string key = "released-ordering-key";
            var circuit = new LlmAuthCircuit(_logger);
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("one"));
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("two"));
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("three"));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                key,
                circuit);

            Func<Task> act = () => provider.CompleteAsync(
                new LlmRequest { Prompt = "hi" },
                canceled.Token);

            await act.Should().ThrowAsync<AuthenticationException>();
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Fact]
        public async Task CompleteAsync_AbsentCredential_SendsNoAuthorizationAndUsesNoAuthGate()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                apiKey: null,
                authCircuit: new LlmAuthCircuit(_logger));

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers.Should().NotContainKey("Authorization");
        }

        [Fact]
        public async Task CheckHealthAsync_TransportMessageIsReturnedVerbatim_CurrentPrivacyDebt()
        {
            const string credential = "health-secret-marker";
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("failure included " + credential));
            var provider = CreateProvider(credential);

            var result = await provider.CheckHealthAsync();

            result.StatusMessage.Should().Contain(credential,
                "the released adapter currently exposes the raw transport message; adoption must explicitly preserve or correct this behavior");
        }

        private BrainarrOpenAiCompatibleProvider CreateProvider(string? apiKey = null)
            => new(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                apiKey);
    }
}
