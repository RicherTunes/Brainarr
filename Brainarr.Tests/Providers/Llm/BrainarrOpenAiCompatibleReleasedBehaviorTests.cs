using System;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Reflection;
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
using NzbDrone.Core.ImportLists.Brainarr.Services;
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

        public static TheoryData<float?, string> ReleasedTemperatureBodies => new()
        {
            { null, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":0.7,\"max_tokens\":2000,\"stream\":false}" },
            { 0.0f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":0.0,\"max_tokens\":2000,\"stream\":false}" },
            { -0.0f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":-0.0,\"max_tokens\":2000,\"stream\":false}" },
            { 1.0f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":1.0,\"max_tokens\":2000,\"stream\":false}" },
            { 0.2f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":0.20000000298023224,\"max_tokens\":2000,\"stream\":false}" },
            { 0.33333334f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":0.3333333432674408,\"max_tokens\":2000,\"stream\":false}" },
            { 1.0e-20f, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":9.999999682655225E-21,\"max_tokens\":2000,\"stream\":false}" },
            { float.NaN, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":\"NaN\",\"max_tokens\":2000,\"stream\":false}" },
            { float.PositiveInfinity, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":\"Infinity\",\"max_tokens\":2000,\"stream\":false}" },
            { float.NegativeInfinity, "{\"model\":\"model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"temperature\":\"-Infinity\",\"max_tokens\":2000,\"stream\":false}" },
        };

        [Theory]
        [MemberData(nameof(ReleasedTemperatureBodies))]
        public async Task CompleteAsync_Temperature_WritesExactReleasedBody(float? temperature, string expectedBody)
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await CreateProvider().CompleteAsync(new LlmRequest { Prompt = "hi", Temperature = temperature });

            Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>()).Should().Be(expectedBody);
        }

        [Fact]
        public async Task CompleteAsync_EscapingAndSystemPrompt_WriteExactReleasedBody()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await CreateProvider().CompleteAsync(new LlmRequest
            {
                Prompt = "astral 🎵 <tag> \\\"quote\\\" \\\\ slash\nline\tend\u0001",
                SystemPrompt = "system",
                Temperature = 1.0f,
                MaxTokens = 17,
            });

            Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>()).Should().Be(
                "{\"model\":\"model\",\"messages\":[{\"role\":\"system\",\"content\":\"system\"},{\"role\":\"user\",\"content\":\"astral 🎵 <tag> \\\\\\\"quote\\\\\\\" \\\\\\\\ slash\\nline\\tend\\u0001\"}],\"temperature\":1.0,\"max_tokens\":17,\"stream\":false}");
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
        public async Task CompleteAsync_RequestTimeout_IsIgnoredInFavorOfExactAmbientOwnerTimeout()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            using (TimeoutContext.Push(120))
            {
                await CreateProvider().CompleteAsync(new LlmRequest
                {
                    Prompt = "hi",
                    Timeout = TimeSpan.FromSeconds(1),
                });
            }

            captured!.RequestTimeout.Should().Be(TimeSpan.FromSeconds(120));
        }

        [Theory]
        [InlineData(0.0f, "\"temperature\":0.0")]
        [InlineData(-0.0f, "\"temperature\":-0.0")]
        [InlineData(1.0f, "\"temperature\":1.0")]
        [InlineData(0.33333334f, "\"temperature\":0.3333333432674408")]
        [InlineData(float.NaN, "\"temperature\":\"NaN\"")]
        [InlineData(float.PositiveInfinity, "\"temperature\":\"Infinity\"")]
        public async Task CompleteAsync_FiniteAndNonfiniteFloatTemperature_PreservesReleasedWireValue(
            float temperature,
            string expectedFragment)
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await CreateProvider().CompleteAsync(new LlmRequest
            {
                Prompt = "hi",
                Temperature = temperature,
            });

            Encoding.UTF8.GetString(captured!.ContentData ?? Array.Empty<byte>())
                .Should().Contain(expectedFragment);
        }

        [Theory]
        [InlineData("{\"choices\":[]}", "", false)]
        [InlineData("{\"choices\":[{\"message\":{}}],\"usage\":null}", "", false)]
        [InlineData("{\"choices\":{},\"usage\":[]}", "{\"choices\":{},\"usage\":[]}", false)]
        [InlineData("[]", "[]", false)]
        [InlineData("{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":0,\"completion_tokens\":0}}", "ok", true)]
        public async Task CompleteAsync_DtoAndUsageShapes_PreserveReleasedFallback(
            string payload,
            string expectedContent,
            bool expectsUsage)
        {
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(payload));

            var response = await CreateProvider().CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be(expectedContent);
            (response.Usage is not null).Should().Be(expectsUsage);
        }

        [Theory]
        [InlineData("{}", "", false)]
        [InlineData("{\"choices\":null}", "", false)]
        [InlineData("{\"choices\":[]}", "", false)]
        [InlineData("{\"choices\":{}}", "{\"choices\":{}}", false)]
        [InlineData("{\"choices\":[\"wrong\"]}", "{\"choices\":[\"wrong\"]}", false)]
        [InlineData("{\"choices\":[{}]}", "", false)]
        [InlineData("{\"choices\":[{\"message\":null}]}", "", false)]
        [InlineData("{\"choices\":[{\"message\":\"wrong\"}]}", "{\"choices\":[{\"message\":\"wrong\"}]}", false)]
        [InlineData("{\"choices\":[{\"message\":{}}]}", "", false)]
        [InlineData("{\"choices\":[{\"message\":{\"content\":null}}]}", "", false)]
        [InlineData("{\"choices\":[{\"message\":{\"content\":123}}]}", "123", false)]
        [InlineData("{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":null}", "ok", false)]
        [InlineData("{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":[]}", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":[]}", false)]
        public async Task CompleteAsync_EachDtoShape_HasIndependentReleasedOutcome(
            string payload,
            string expectedContent,
            bool expectsUsage)
        {
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(payload));

            var response = await CreateProvider().CompleteAsync(new LlmRequest { Prompt = "hi" });

            response.Content.Should().Be(expectedContent);
            (response.Usage is not null).Should().Be(expectsUsage);
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
        public async Task CompleteAsync_AbsentCredential_SendsNoAuthorizationHeader()
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

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationException), LlmErrorCode.AuthenticationFailed)]
        [InlineData(HttpStatusCode.Forbidden, typeof(AuthenticationException), LlmErrorCode.AuthorizationFailed)]
        [InlineData((HttpStatusCode)429, typeof(RateLimitException), LlmErrorCode.RateLimited)]
        public async Task CompleteAsync_KeyedHttpFailure_PreservesReleasedExceptionMapping(
            HttpStatusCode status,
            Type expectedType,
            LlmErrorCode expectedCode)
        {
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(status, "failure"));

            Func<Task> act = () => CreateProvider("mapped-key")
                .CompleteAsync(new LlmRequest { Prompt = "hi" });

            var thrown = await act.Should().ThrowAsync<LlmProviderException>();
            thrown.Which.Should().BeOfType(expectedType);
            thrown.Which.ErrorCode.Should().Be(expectedCode);
        }

        [Fact]
        public async Task CompleteAsync_KeyedUnauthorizedResponses_OpenCircuitAfterThreeRoundTrips()
        {
            const string key = "double-record-key";
            var circuit = new LlmAuthCircuit(_logger);
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                key,
                circuit);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(HttpStatusCode.Unauthorized));

            await FluentActions.Awaiting(() => provider.CompleteAsync(new LlmRequest { Prompt = "one" }))
                .Should().ThrowAsync<AuthenticationException>();
            circuit.IsOpen("openai-compatible", key, out _).Should().BeFalse();
            await FluentActions.Awaiting(() => provider.CompleteAsync(new LlmRequest { Prompt = "two" }))
                .Should().ThrowAsync<AuthenticationException>();
            circuit.IsOpen("openai-compatible", key, out _).Should().BeFalse();
            await FluentActions.Awaiting(() => provider.CompleteAsync(new LlmRequest { Prompt = "three" }))
                .Should().ThrowAsync<AuthenticationException>();

            circuit.IsOpen("openai-compatible", key, out _).Should().BeTrue(
                "the released AuthenticationException catch preserves one circuit record per returned 401 response");
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Exactly(3));
        }

        [Fact]
        public async Task CompleteAsync_AbsentCredential_DoesNotCreateStateInSuppliedCircuit()
        {
            var circuit = new LlmAuthCircuit(_logger);
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object,
                _logger,
                "https://compatible.example",
                "model",
                apiKey: null,
                authCircuit: circuit);

            await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            GetCircuitEntryCount(circuit).Should().Be(0,
                "absent auth must not consult or record success in the supplied real circuit");
        }

        [Fact]
        public async Task CompleteAsync_MixedCredential_StripsHeaderControlCharacters()
        {
            HttpRequest? captured = null;
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                .Callback<HttpRequest>(request => captured = request)
                .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));

            await CreateProvider("ab\r\n\0cd").CompleteAsync(new LlmRequest { Prompt = "hi" });

            captured!.Headers["Authorization"].Should().Be("Bearer abcd");
        }

        [Fact]
        public async Task CompleteAsync_RateLimit_PreservesRetryAfterMetadata()
        {
            var response = Brainarr.Tests.Helpers.HttpResponseFactory.Error((HttpStatusCode)429, "slow down");
            response.Headers["Retry-After"] = "7";
            _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>())).ReturnsAsync(response);

            Func<Task> act = () => CreateProvider("retry-key")
                .CompleteAsync(new LlmRequest { Prompt = "hi" });

            var thrown = await act.Should().ThrowAsync<RateLimitException>();
            thrown.Which.ErrorCode.Should().Be(LlmErrorCode.RateLimited);
            thrown.Which.IsRetryable.Should().BeTrue();
            thrown.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public void PublicConstructors_PreserveReleasedSignatures()
        {
            var signatures = typeof(BrainarrOpenAiCompatibleProvider).GetConstructors()
                .Select(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
                .ToArray();

            signatures.Should().Contain(signature => signature.SequenceEqual(new[]
                { typeof(IHttpClient), typeof(Logger), typeof(string), typeof(string), typeof(string) }));
            signatures.Should().Contain(signature => signature.SequenceEqual(new[]
                { typeof(IHttpClient), typeof(Logger), typeof(string), typeof(string), typeof(string), typeof(LlmAuthCircuit) }));
        }

        [Fact]
        public void PublicSurfaceAndDefaults_PreserveReleasedContract()
        {
            var type = typeof(BrainarrOpenAiCompatibleProvider);
            type.IsPublic.Should().BeTrue();
            type.IsSealed.Should().BeTrue();
            type.GetInterfaces().Should().Contain(new[]
            {
                typeof(ILlmProvider),
                typeof(IBrainarrLlmHintSource),
                typeof(IBrainarrLlmModelMutable),
            });

            var fiveParameterConstructor = type.GetConstructors()
                .Single(constructor => constructor.GetParameters().Length == 5);
            var fiveParameters = fiveParameterConstructor.GetParameters();
            fiveParameters.Select(parameter => parameter.Name).Should().Equal(
                "httpClient", "logger", "baseUrl", "model", "apiKey");
            fiveParameters[4].HasDefaultValue.Should().BeTrue();
            fiveParameters[4].DefaultValue.Should().BeNull();

            var sixParameters = type.GetConstructors()
                .Single(constructor => constructor.GetParameters().Length == 6)
                .GetParameters();
            sixParameters.Select(parameter => parameter.Name).Should().Equal(
                "httpClient", "logger", "baseUrl", "model", "apiKey", "authCircuit");
            sixParameters.Should().OnlyContain(parameter => !parameter.HasDefaultValue);

            var provider = CreateProvider();
            provider.ProviderId.Should().Be("openai-compatible");
            provider.DisplayName.Should().Be("OpenAI-Compatible");
            provider.Capabilities.Flags.Should().Be(
                LlmCapabilityFlags.TextCompletion |
                LlmCapabilityFlags.Streaming |
                LlmCapabilityFlags.SystemPrompt);
            provider.Capabilities.UsesOpenAiCompatibleApi.Should().BeTrue();
            provider.Capabilities.Flags.HasFlag(LlmCapabilityFlags.JsonMode).Should().BeFalse();
            provider.StreamAsync(new LlmRequest { Prompt = "hi" }).Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\0")]
        [InlineData("\r\n\0")]
        public async Task CompleteAsync_GhostCredential_PreflightStillPrecedesCallerCancellation(string credential)
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();

            Func<Task> act = () => CreateProvider(credential).CompleteAsync(
                new LlmRequest { Prompt = "hi" },
                canceled.Token);

            if (string.IsNullOrWhiteSpace(credential))
            {
                await act.Should().ThrowExactlyAsync<OperationCanceledException>();
            }
            else
            {
                await act.Should().ThrowAsync<ArgumentException>()
                    .WithParameterName("apiKey");
            }

            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
        }

        public static TheoryData<HttpStatusCode?, bool> AbsentCredentialOutcomes => new()
        {
            { HttpStatusCode.OK, false },
            { HttpStatusCode.Unauthorized, false },
            { HttpStatusCode.Forbidden, false },
            { (HttpStatusCode)429, false },
            { HttpStatusCode.InternalServerError, false },
            { null, true },
        };

        [Theory]
        [MemberData(nameof(AbsentCredentialOutcomes))]
        public async Task CompleteAsync_AbsentCredential_PerformsZeroAuthCircuitCallbacks(
            HttpStatusCode? status,
            bool transportThrows)
        {
            var circuit = new LlmAuthCircuit(_logger);
            if (transportThrows)
            {
                _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ThrowsAsync(new HttpRequestException("offline"));
            }
            else if (status == HttpStatusCode.OK)
            {
                _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Ok(
                        "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}"));
            }
            else
            {
                _http.Setup(x => x.ExecuteAsync(It.IsAny<HttpRequest>()))
                    .ReturnsAsync(Brainarr.Tests.Helpers.HttpResponseFactory.Error(status!.Value, "failure"));
            }

            var provider = new BrainarrOpenAiCompatibleProvider(
                _http.Object, _logger, "https://compatible.example", "model", null, circuit);
            try
            {
                await provider.CompleteAsync(new LlmRequest { Prompt = "hi" });
            }
            catch (LlmProviderException)
            {
            }

            GetCircuitEntryCount(circuit).Should().Be(0,
                "each released circuit callback creates an entry before doing any bookkeeping, so zero entries proves zero callback invocations for absent auth");
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public void LlmAuthCircuit_CallbacksExposeReleasedInvalidKeyExceptions()
        {
            var circuit = new LlmAuthCircuit(_logger);

            FluentActions.Invoking(() => circuit.IsOpen("openai-compatible", "\0", out _))
                .Should().Throw<ArgumentException>().WithParameterName("apiKey");
            FluentActions.Invoking(() => circuit.RecordAuthFailure("openai-compatible", "\0"))
                .Should().Throw<ArgumentException>().WithParameterName("apiKey");
            FluentActions.Invoking(() => circuit.RecordSuccess("openai-compatible", "\0"))
                .Should().Throw<ArgumentException>().WithParameterName("apiKey");
            GetCircuitEntryCount(circuit).Should().Be(0);
        }

        [Fact]
        public void LlmAuthCircuit_EachSuccessfulCallbackCreatesObservableEntry()
        {
            foreach (var callback in new Action<LlmAuthCircuit>[]
            {
                circuit => circuit.IsOpen("openai-compatible", "key", out _),
                circuit => circuit.RecordAuthFailure("openai-compatible", "key"),
                circuit => circuit.RecordSuccess("openai-compatible", "key"),
            })
            {
                var circuit = new LlmAuthCircuit(_logger);
                callback(circuit);
                GetCircuitEntryCount(circuit).Should().Be(1);
            }
        }

        [Fact]
        public async Task CompleteAsync_NulOnlyCredential_PreservesRawMakeKeyFailureBeforeTransport()
        {
            var provider = CreateProvider("\0");

            Func<Task> act = () => provider.CompleteAsync(new LlmRequest { Prompt = "hi" });

            await act.Should().ThrowAsync<ArgumentException>()
                .WithParameterName("apiKey");
            _http.Verify(x => x.ExecuteAsync(It.IsAny<HttpRequest>()), Times.Never);
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

        private static int GetCircuitEntryCount(LlmAuthCircuit circuit)
        {
            var field = typeof(LlmAuthCircuit).GetField("_gates", BindingFlags.Instance | BindingFlags.NonPublic);
            var entries = field!.GetValue(circuit)!;
            return (int)entries.GetType().GetProperty("Count")!.GetValue(entries)!;
        }
    }
}
