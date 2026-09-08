using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Defines separately reviewed safety corrections for a future shared-base adoption.
    /// These are intentional behavior-change reds against the standalone provider, not
    /// released-behavior characterization.
    /// </summary>
    public sealed class BrainarrOpenAiCompatibleDesiredPolicyTests
    {
        private readonly Mock<IHttpClient> _http = new();
        private readonly Logger _logger = Brainarr.Tests.Helpers.TestLogger.CreateNullLogger();

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\0")]
        [InlineData("\r\n\0")]
        public async Task CompleteAsync_PostSanitizationBlankCredential_IsAbsent(string credential)
        {
            HttpRequest? captured = null;
            var circuit = new LlmAuthCircuit(_logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                credential,
                circuit);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers.Should().NotContainKey("Authorization");
            GetCircuitEntryCount(circuit).Should().Be(0,
                "a normalized absent credential must not consult or update auth-circuit state");
        }

        [Fact]
        public async Task CompleteAsync_CallerCancellation_WinsBeforeOpenAuthCircuit()
        {
            const string key = "ordering-secret";
            var circuit = new LlmAuthCircuit(_logger);
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("one"));
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("two"));
            circuit.RecordAuthFailure("openai-compatible", key, new Exception("three"));
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                key,
                circuit);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            Func<Task> act = () => provider.CompleteAsync(
                new LlmRequest { Prompt = "hi" },
                canceled.Token);

            await act.Should().ThrowExactlyAsync<OperationCanceledException>();
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Theory]
        [InlineData("\0")]
        [InlineData("\r\n\0")]
        public async Task CompleteAsync_CallerCancellation_WinsBeforeGhostCredentialCircuitFailure(string credential)
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                credential,
                new LlmAuthCircuit(_logger));

            Func<Task> act = () => provider.CompleteAsync(
                new LlmRequest { Prompt = "hi" },
                canceled.Token);

            await act.Should().ThrowExactlyAsync<OperationCanceledException>();
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        [Fact]
        public async Task CheckHealthAsync_TransportFailure_RedactsConfiguredCredential()
        {
            const string credential = "health-secret-marker";
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ThrowsAsync(new HttpRequestException("failure included " + credential));
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                credential);

            var result = await provider.CheckHealthAsync();

            result.StatusMessage.Should().NotContain(credential);
            result.StatusMessage.Should().Contain("[REDACTED]");
            result.IsHealthy.Should().BeTrue("connection failure remains Degraded rather than Unhealthy");
            result.StatusMessage.Should().StartWith("[Degraded]");
            result.Provider.Should().Be("openai-compatible");
            result.AuthMethod.Should().Be("apiKey");
            result.Model.Should().Be("model");
            result.ErrorCode.Should().Be("ConnectionFailed");
        }

        private static int GetCircuitEntryCount(LlmAuthCircuit circuit)
        {
            var field = typeof(LlmAuthCircuit).GetField("_gates", BindingFlags.Instance | BindingFlags.NonPublic);
            var entries = field!.GetValue(circuit)!;
            return (int)entries.GetType().GetProperty("Count")!.GetValue(entries)!;
        }
    }
}
