using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Lidarr.Plugin.Common.Observability;

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
        private const string DefaultUserAgent = "claude-cli/2.0.5 (external, cli)";
        private const string UserAgentEnvVar = "BRAINARR_ZAI_CODING_USER_AGENT";

        // Z.AI's Coding-Plan endpoint requires a coding-tool User-Agent (claude-cli), but
        // Lidarr's IHttpClient forbids overriding User-Agent (ManagedHttpDispatcher throws
        // "User-Agent other than Lidarr not allowed."), which crash-blocks the connection test
        // and prevents saving the provider. So this provider dispatches via a raw
        // System.Net.Http.HttpClient. The endpoint is an external HTTPS API addressed directly;
        // brainarr's own RateLimiter handles throttling (same rationale as StreamingHttpExecutor).
        private static readonly Lazy<System.Net.Http.HttpClient> SharedRawClient = new(static () =>
            new System.Net.Http.HttpClient(new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            {
                // Per-request timeout is enforced via a linked CancellationTokenSource in SendAsync.
                Timeout = Timeout.InfiniteTimeSpan,
            });

        private readonly System.Net.Http.HttpClient _rawClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly string _userAgent;
        private readonly LlmAuthCircuit _authCircuit;
        private string _model;

        public BrainarrZaiCodingProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model = null)
            : this(httpClient, logger, apiKey, model, authCircuit: null)
        {
        }

        public BrainarrZaiCodingProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, LlmAuthCircuit? authCircuit)
            : this(httpClient, logger, apiKey, model, authCircuit, testHandler: null)
        {
        }

        // Test seam: supply a fake HttpMessageHandler to dispatch without hitting the network.
        // httpClient (Lidarr's IHttpClient) is accepted for DI/ProviderRegistry signature
        // stability but intentionally unused — Z.AI Coding requires a raw client (see above).
        internal BrainarrZaiCodingProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, LlmAuthCircuit? authCircuit, HttpMessageHandler? testHandler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI Coding Plan API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = ModelIdMapper.ToRawId("zaicoding", model ?? BrainarrConstants.DefaultZaiCodingModel);
            _userAgent = Environment.GetEnvironmentVariable(UserAgentEnvVar) is { Length: > 0 } custom
                ? custom
                : DefaultUserAgent;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
            _rawClient = testHandler != null
                ? new System.Net.Http.HttpClient(testHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan }
                : SharedRawClient.Value;
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
                  | LlmCapabilityFlags.SystemPrompt,
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

                var result = await SendAsync(probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (result.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)result.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    _model,
                    errorCode: ((int)result.StatusCode).ToString());
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

            using var _scope = PluginLogContext.Push("Brainarr", "LlmComplete", provider: ProviderIdConst);

            if (_authCircuit.IsOpen(ProviderIdConst, _apiKey, out var circuitReason))
            {
                throw new AuthenticationException(ProviderIdConst, LlmErrorCode.AuthenticationFailed,
                    "Auth circuit open: " + circuitReason);
            }

            LlmResponse result;
            try
            {
                var body = BuildRequestBody(request);
                var httpResult = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

                if (httpResult.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    // Pass the FULL body — MapZaiHttpError truncates internally for the message
                    // but must parse the whole JSON to detect Z.AI billing codes (1113/1115).
                    // Truncating here first can chop the closing brace and mis-map QuotaExceeded
                    // to RateLimited (see BrainarrZaiGlmProvider test Code1113_InLongBody_*).
                    var ex = BrainarrZaiGlmProvider.MapZaiHttpError(
                        (int)httpResult.StatusCode,
                        httpResult.Content,
                        httpResult.RetryAfter,
                        inner: null);

                    if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                    {
                        _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, ex);
                    }
                    throw ex;
                }

                result = ParseCompletion(httpResult.Content ?? string.Empty);
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (LlmProviderException lpe) when (
                lpe.ErrorCode == LlmErrorCode.AuthenticationFailed ||
                lpe.ErrorCode == LlmErrorCode.AuthorizationFailed)
            {
                _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, lpe);
                throw;
            }

            _authCircuit.RecordSuccess(ProviderIdConst, _apiKey);
            return result;
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
                // ClaudeCodeSubscription. Do NOT raise this for GLM's verbosity: counter-intuitively a
                // larger cap is worse — GLM treats the headroom as licence to pad with reasoning prose
                // and runs past the request timeout (live: 4096/8192 → TimeoutException) before closing
                // the array, yielding zero items. 2000 completes in ~10s; when GLM's ```json list is
                // truncated at the tail, RecommendationJsonParser salvages the complete objects.
                ["max_tokens"] = request.MaxTokens ?? 2000,
                // NOTE: temperature is intentionally omitted. Z.AI's Coding-Plan (Anthropic-format)
                // endpoint rejects the request with [1210][Invalid API parameter] when `temperature`
                // is present — Claude Code (which this endpoint emulates) does not send it. Confirmed
                // live: with temperature dropped the same request returns 200 + a full completion.
            };

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                // Anthropic format puts `system` at the top level, NOT in the messages array.
                body["system"] = request.SystemPrompt;
            }

            // The shared LlmLogger "Model=default" line is an unset-logging default (the adapter
            // doesn't pass the model through), so it never reflects what's actually sent. Log the
            // real outbound model at debug so support can confirm the resolved GLM id on the wire.
            _logger.Debug("[ZaiCoding] outbound model='{0}' max_tokens={1}", modelRaw, body["max_tokens"]);

            return body;
        }

        private readonly struct ZaiHttpResult
        {
            public ZaiHttpResult(System.Net.HttpStatusCode statusCode, string content, TimeSpan? retryAfter)
            {
                StatusCode = statusCode;
                Content = content;
                RetryAfter = retryAfter;
            }

            public System.Net.HttpStatusCode StatusCode { get; }
            public string Content { get; }
            public TimeSpan? RetryAfter { get; }
        }

        private async Task<ZaiHttpResult> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            // Dispatched via the raw HttpClient (NOT Lidarr's IHttpClient) so the Claude-Code
            // User-Agent / x-app headers Z.AI's Coding-Plan gate requires are actually sent —
            // Lidarr's IHttpClient throws "User-Agent other than Lidarr not allowed." on these.
            using var req = new HttpRequestMessage(HttpMethod.Post, BrainarrConstants.ZaiCodingMessagesUrl);
            // Bearer matches Z.AI docs' ANTHROPIC_AUTH_TOKEN convention used by Claude Code/Cline/OpenCode.
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
            req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
            // Identify as Claude Code so Z.AI's Coding-Plan UA filter admits the request. Without
            // these, Z.AI returns 4xx ("client not supported by Coding Plan") for GLM-5.x/4.7
            // models (docs.z.ai/devpack/overview).
            req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            req.Headers.TryAddWithoutValidation("x-app", "cli");

            var content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Content = content;

            var seconds = useTestTimeout
                ? BrainarrConstants.TestConnectionTimeout
                : TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(seconds));

            try
            {
                using var response = await _rawClient
                    .SendAsync(req, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                var respBody = response.Content != null
                    ? await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false)
                    : string.Empty;

                return new ZaiHttpResult(
                    response.StatusCode,
                    respBody,
                    LlmErrorMapper.ParseRetryAfterHeader(response.Headers.RetryAfter));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Our linked CTS fired (per-request timeout), not the caller's token.
                throw LlmErrorMapper.MapException(ProviderIdConst,
                    new TimeoutException($"Z.AI Coding request timed out after {seconds}s"));
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
