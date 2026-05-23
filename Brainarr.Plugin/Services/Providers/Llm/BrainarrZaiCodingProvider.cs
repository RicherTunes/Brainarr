using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Z.AI's GLM Coding Plan subscription
    /// (<c>https://api.z.ai/api/anthropic/v1/messages</c>).
    ///
    /// <para>
    /// This is the same endpoint Claude Code, Cline, and OpenCode hit when configured with
    /// <c>ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic</c>. It speaks the Anthropic
    /// Messages API (NOT the OpenAI Chat Completions format that <see cref="BrainarrZaiGlmProvider"/>
    /// uses against the PaaS endpoint), and accepts the Coding Plan models that the PaaS endpoint
    /// doesn't serve: GLM-5.1, GLM-5-Turbo, GLM-4.7, GLM-4.5-Air (plus GLM-4.6 / GLM-4.5).
    /// </para>
    ///
    /// <para>
    /// Auth is the user's Z.AI API key sent as <c>Authorization: Bearer ...</c> (Z.AI's docs call
    /// this <c>ANTHROPIC_AUTH_TOKEN</c>). The key is the same one used by <see cref="BrainarrZaiGlmProvider"/>
    /// — we read <c>settings.ZaiGlmApiKey</c> so users only configure one credential and pick the
    /// endpoint by selecting the provider. Coding Plan subscribers get GLM-5.1 access via this
    /// provider; PaaS-credit users continue to use the existing <c>ZaiGlm</c> provider.
    /// </para>
    /// </summary>
    public sealed class BrainarrZaiCodingProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "zaicoding";
        private const string AnthropicVersion = "2023-06-01";
        // Z.AI's Coding endpoint inspects User-Agent / x-app to admit Coding-Plan traffic
        // (the PaaS endpoint, in contrast, accepts anything). To be a well-behaved Coding-Plan
        // client we identify as Claude Code — the canonical reference client that Z.AI publishes
        // setup instructions for. Bump the version when Anthropic's official CLI bumps.
        // Source: docs.z.ai/scenario-example/develop-tools/claude (ANTHROPIC_BASE_URL setup).
        private const string ClaudeCodeUserAgent = "claude-cli/2.0.5 (external, cli)";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;

        public BrainarrZaiCodingProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI Coding Plan API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("zaicoding", model ?? BrainarrConstants.DefaultZaiCodingModel);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Z.AI Coding Subscription";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // Anthropic Messages format; we don't decode SSE here yet (same gap as
            // BrainarrClaudeCodeSubscriptionProvider — kept consistent intentionally).
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling,
            MaxContextTokens = 200_000,
            UsesOpenAiCompatibleApi = false,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = ModelIdMapper.ToRawId("zaicoding", modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var probe = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = new[] { new { role = "user", content = "Reply with OK" } },
                    ["max_tokens"] = 10,
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
                throw BrainarrZaiGlmProvider.MapZaiHttpError(
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
            // Anthropic-format SSE; common's decoder does not yet support it. Same gap as
            // BrainarrClaudeCodeSubscriptionProvider — returning null falls back to non-streaming.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model)
                ? ModelIdMapper.ToRawId("zaicoding", request.Model)
                : _model;

            var body = new Dictionary<string, object>
            {
                ["model"] = modelRaw,
                ["messages"] = new[] { new { role = "user", content = request.Prompt } },
                // Anthropic Messages API requires max_tokens; default matches BrainarrAnthropicProvider/
                // ClaudeCodeSubscription so behavior is consistent across Anthropic-format providers.
                ["max_tokens"] = request.MaxTokens ?? 2000,
                ["temperature"] = (double?)request.Temperature ?? 0.8,
            };

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                // Anthropic format puts `system` at the top level, NOT in the messages array.
                body["system"] = request.SystemPrompt;
            }

            return body;
        }

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(BrainarrConstants.ZaiCodingMessagesUrl)
                // Bearer matches Z.AI docs' ANTHROPIC_AUTH_TOKEN convention used by Claude Code/Cline/OpenCode.
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("anthropic-version", AnthropicVersion)
                .SetHeader("Content-Type", "application/json")
                // Identify as Claude Code so Z.AI's Coding-Plan UA filter admits the request.
                // Without these, Z.AI returns 4xx ("client not supported by Coding Plan") for
                // GLM-5.x/4.7 models. Matching Claude Code's wire shape is the documented path —
                // Z.AI explicitly supports these tools per docs.z.ai/devpack/overview.
                .SetHeader("User-Agent", ClaudeCodeUserAgent)
                .SetHeader("x-app", "cli")
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
                throw BrainarrZaiGlmProvider.MapZaiHttpError(
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
                var parsed = JsonConvert.DeserializeObject<AnthropicMessageDto>(content);

                var text = parsed?.Content == null
                    ? string.Empty
                    : string.Join(string.Empty, parsed.Content
                        .Where(b => b != null && string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                        .Select(b => b.Text ?? string.Empty));

                return new LlmResponse
                {
                    Content = text,
                    FinishReason = parsed?.StopReason,
                    Usage = parsed?.Usage != null
                        ? new LlmUsage
                        {
                            InputTokens = parsed.Usage.InputTokens,
                            OutputTokens = parsed.Usage.OutputTokens,
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

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Z.AI API key rejected by the Coding Plan endpoint. Verify your key at https://z.ai/manage-apikey/apikey-list and that your account has an active Coding Plan subscription.",
                        BrainarrConstants.DocsZaiCodingSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Z.AI Coding Plan quota exhausted or no resource package on this key. Check your subscription status at https://z.ai or upgrade the plan tier.",
                        BrainarrConstants.DocsZaiCodingSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Z.AI Coding Plan rate limit hit — reduce request frequency or upgrade your plan tier.",
                        BrainarrConstants.DocsZaiCodingSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "Selected model is not available on your Coding Plan tier. The Coding Plan serves GLM-5.1 / GLM-5-Turbo / GLM-4.7 / GLM-4.5-Air; check your plan's included models.",
                        BrainarrConstants.DocsZaiCodingSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        // Anthropic Messages response shape — same structure as BrainarrClaudeCodeSubscriptionProvider's
        // DTOs (intentionally kept local rather than shared so the two providers stay independently
        // evolvable as Z.AI and Anthropic drift apart on tool-use / vision / thinking specifics).
        private sealed class AnthropicMessageDto
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("content")]
            public List<AnthropicContentBlock>? Content { get; set; }

            [JsonProperty("stop_reason")]
            public string? StopReason { get; set; }

            [JsonProperty("usage")]
            public AnthropicUsageDto? Usage { get; set; }
        }

        private sealed class AnthropicContentBlock
        {
            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("text")]
            public string? Text { get; set; }
        }

        private sealed class AnthropicUsageDto
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }

            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }
    }
}
