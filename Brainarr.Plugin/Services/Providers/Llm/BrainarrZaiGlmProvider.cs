using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Streaming.Decoders;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Z.AI (Zhipu) GLM
    /// (<c>https://api.z.ai/api/paas/v4/chat/completions</c>).
    ///
    /// <para>
    /// Z.AI's API speaks the OpenAI Chat Completions wire format with
    /// <c>response_format = {"type":"json_object"}</c> for strict-JSON output.
    /// Lineup as of May 2026 (verified against docs.z.ai):
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>glm-5.1</c> — flagship, 200K context, 128K max output, long-horizon agent</item>
    /// <item><c>glm-5</c> — 745B MoE, released Feb 2026</item>
    /// <item><c>glm-5-turbo</c> — fast/coding variant</item>
    /// <item><c>glm-4.7</c> — multilingual coding gains over 4.6</item>
    /// <item><c>glm-4.6</c> — 200K context, was flagship before 4.7</item>
    /// <item><c>glm-4.5</c> — 355B params, agent-oriented</item>
    /// <item><c>glm-4.5-air</c> — 106B params, balanced cost/quality (default)</item>
    /// <item><c>glm-4-32b-0414-128k</c> — 32B parameter variant, 128K context</item>
    /// </list>
    /// </summary>
    public sealed class BrainarrZaiGlmProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "zaiglm";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private string _model;

        public BrainarrZaiGlmProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null)
        {
        }

        public BrainarrZaiGlmProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("zaiglm", model ?? BrainarrConstants.DefaultZaiGlmModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Z.AI GLM";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // GLM-5.1 and GLM-4.6 explicitly support 200K context + JSON mode + tool
            // calling per Z.AI docs. Older models (4.5 family) support TextCompletion
            // + Streaming + JsonMode but have shorter contexts. Capabilities are
            // declared at the provider level so we declare the union; per-model
            // limits are enforced server-side.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId("zaiglm", modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var probe = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5,
                };

                var response = await SendAsync(probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(
                    lpe.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model,
                    errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(
                    ex.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model);
            }
        }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var body = BuildRequestBody(request);
            var response = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw MapZaiHttpError(
                    (int)response.StatusCode,
                    Truncate(response.Content),
                    BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                    inner: null);
            }

            return ParseCompletion(response.Content ?? string.Empty);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return StreamAsyncCore(request, cancellationToken);
        }

        private async IAsyncEnumerable<LlmStreamChunk> StreamAsyncCore(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var body = BuildStreamingRequestBody(request);
            var headers = new[]
            {
                new KeyValuePair<string, string>("Authorization", $"Bearer {_apiKey}"),
                new KeyValuePair<string, string>("Accept", "text/event-stream"),
            };

            var stream = await _streamingExecutor.SendForStreamingAsync(
                ProviderIdConst,
                HttpMethod.Post,
                BrainarrConstants.ZaiGlmChatCompletionsUrl,
                headers,
                JsonConvert.SerializeObject(body),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await using (stream)
            {
                var decoder = new OpenAiStreamDecoder();
                await foreach (var chunk in decoder.DecodeAsync(stream, cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }
            }
        }

        private object BuildStreamingRequestBody(LlmRequest request)
        {
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("zaiglm", request.Model)
                : _model;
            object[] messages = string.IsNullOrEmpty(request.SystemPrompt)
                ? new object[] { new { role = "user", content = request.Prompt } }
                : new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.Prompt },
                };
            var dict = new Dictionary<string, object?>
            {
                ["model"] = modelRaw,
                ["messages"] = messages,
                ["temperature"] = (double?)request.Temperature ?? 0.7,
                ["max_tokens"] = request.MaxTokens ?? 2000,
                ["stream"] = true,
            };
            if (request.JsonMode) dict["response_format"] = new { type = "json_object" };
            return dict;
        }

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.7;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("zaiglm", request.Model)
                : _model;

            object? responseFormat = request.JsonMode ? new { type = "json_object" } : null;

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                if (responseFormat != null)
                {
                    return new
                    {
                        model = modelRaw,
                        messages = new[]
                        {
                            new { role = "system", content = request.SystemPrompt },
                            new { role = "user", content = request.Prompt },
                        },
                        temperature = temp,
                        max_tokens = maxTokens,
                        stream = false,
                        response_format = responseFormat,
                    };
                }

                return new
                {
                    model = modelRaw,
                    messages = new[]
                    {
                        new { role = "system", content = request.SystemPrompt },
                        new { role = "user", content = request.Prompt },
                    },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false,
                };
            }

            if (responseFormat != null)
            {
                return new
                {
                    model = modelRaw,
                    messages = new[] { new { role = "user", content = request.Prompt } },
                    temperature = temp,
                    max_tokens = maxTokens,
                    stream = false,
                    response_format = responseFormat,
                };
            }

            return new
            {
                model = modelRaw,
                messages = new[] { new { role = "user", content = request.Prompt } },
                temperature = temp,
                max_tokens = maxTokens,
                stream = false,
            };
        }

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(BrainarrConstants.ZaiGlmChatCompletionsUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(body));

            var seconds = useTestTimeout
                ? BrainarrConstants.TestConnectionTimeout
                : TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(seconds);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                throw MapZaiHttpError(
                    (int)hex.Response.StatusCode,
                    Truncate(hex.Response.Content),
                    BrainarrHttpResponseHelpers.ParseRetryAfter(hex.Response),
                    hex);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not LlmProviderException)
            {
                throw LlmErrorMapper.MapException(ProviderIdConst, ex);
            }
        }

        private static LlmResponse ParseCompletion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmResponse { Content = string.Empty };
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<ZaiGlmChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? string.Empty;

                return new LlmResponse
                {
                    Content = text,
                    FinishReason = choice?.FinishReason,
                    Usage = parsed?.Usage != null
                        ? new LlmUsage
                        {
                            InputTokens = parsed.Usage.PromptTokens,
                            OutputTokens = parsed.Usage.CompletionTokens,
                        }
                        : null,
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        private static string? Truncate(string? body, int max = 500)
        {
            if (string.IsNullOrEmpty(body)) return body;
            return body.Length <= max ? body : body.Substring(0, max);
        }

        // Z.AI returns HTTP 429 for two semantically different conditions:
        //   - real rate limiting (transient, retry-after applies)
        //   - "Insufficient balance or no resource package" (code 1113) — the account
        //     simply has no PaaS credits, so retrying without topping up will never
        //     succeed. The default 429 -> RateLimited mapping sends users down a
        //     misleading "wait and retry" path; intercept those codes and surface
        //     QuotaExceeded so the existing top-up hint fires instead.
        // Coding-Plan tokens hitting this endpoint also produce 1113 — see
        // BrainarrZaiCodingProvider for the alternative endpoint that serves them.
        internal static LlmProviderException MapZaiHttpError(int statusCode, string? body, TimeSpan? retryAfter, Exception? inner)
        {
            if (statusCode == 429 && TryParseZaiErrorCode(body, out var code, out var message))
            {
                if (code == "1113" || code == "1115")
                {
                    var detail = string.IsNullOrWhiteSpace(message)
                        ? "Z.AI account has no PaaS resource package or insufficient balance."
                        : message!;
                    return new ProviderException(ProviderIdConst, LlmErrorCode.QuotaExceeded, detail, inner);
                }
            }

            return LlmErrorMapper.MapHttpError(ProviderIdConst, statusCode, body, retryAfter, inner);
        }

        internal static bool TryParseZaiErrorCode(string? body, out string? code, out string? message)
        {
            code = null;
            message = null;
            if (string.IsNullOrWhiteSpace(body)) return false;

            try
            {
                var env = JsonConvert.DeserializeObject<ZaiErrorEnvelope>(body);
                code = env?.Error?.Code?.ToString();
                message = env?.Error?.Message;
                return !string.IsNullOrEmpty(code);
            }
            catch
            {
                return false;
            }
        }

        private sealed class ZaiErrorEnvelope
        {
            [JsonProperty("error")]
            public ZaiErrorDto? Error { get; set; }
        }

        private sealed class ZaiErrorDto
        {
            [JsonProperty("code")]
            public object? Code { get; set; }

            [JsonProperty("message")]
            public string? Message { get; set; }
        }

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Z.AI API key. Verify your key at https://z.ai/manage-apikey/apikey-list and ensure it is active.",
                        BrainarrConstants.DocsZaiGlmSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Z.AI rate limit exceeded. Wait a few minutes or reduce request frequency. Free-tier accounts have lower rate limits than paid plans.",
                        BrainarrConstants.DocsZaiGlmSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Z.AI PaaS balance is empty (error 1113). The PaaS endpoint is metered separately from the Coding Plan subscription — a Coding Plan token does NOT grant PaaS credits. Either top up PaaS at https://z.ai/manage-apikey/apikey-list, or switch the Brainarr provider to 'Z.AI Coding Subscription' to use your Coding Plan against the Anthropic-compatible endpoint.",
                        BrainarrConstants.DocsZaiGlmSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class ZaiGlmChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<ZaiGlmChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public ZaiGlmUsageDto? Usage { get; set; }
        }

        private sealed class ZaiGlmChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public ZaiGlmMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private sealed class ZaiGlmMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private sealed class ZaiGlmUsageDto
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }
    }
}
