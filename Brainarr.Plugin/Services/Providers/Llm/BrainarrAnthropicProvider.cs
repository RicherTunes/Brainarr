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
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Anthropic's Messages API.
    ///
    /// <para>
    /// Wave-4a foundation provider. Supports the <c>thinking</c> extended-reasoning option
    /// via two paths:
    /// <list type="bullet">
    /// <item>
    /// Phase 5f: <see cref="LlmRequest.Thinking"/> — explicit per-request hint
    /// (<see cref="LlmThinkingMode.Enabled"/> + <see cref="LlmThinkingHint.BudgetTokens"/>).
    /// This is the preferred path for new callers and overrides the legacy sentinel.
    /// </item>
    /// <item>
    /// Legacy back-compat: the <c>#thinking[(tokens=N)]</c> sentinel that brainarr's
    /// settings layer encodes into the model id (still emitted by
    /// <c>ProviderRegistry</c> when <c>ThinkingMode != Off</c>). When no
    /// <see cref="LlmRequest.Thinking"/> is set on a request, the constructor-parsed
    /// sentinel state controls thinking emission.
    /// </item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Streaming: Anthropic uses its own SSE event types (<c>message_start</c>,
    /// <c>content_block_delta</c> (text + thinking), <c>message_delta</c>, <c>message_stop</c>).
    /// Phase 5b promotion: common now ships <c>AnthropicStreamDecoder</c>, so the
    /// <see cref="LlmCapabilityFlags.Streaming"/> flag is now exposed. The actual
    /// <see cref="StreamAsync"/> implementation still returns null pending the
    /// host-<c>IHttpClient</c> → <see cref="System.IO.Stream"/> bridge that wave-4a/4b
    /// providers also defer (the host buffers full responses today). When that bridge
    /// lands, wiring is a one-line change to <c>new AnthropicStreamDecoder().DecodeAsync(stream)</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrAnthropicProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "anthropic";
        private const string AnthropicVersion = "2023-06-01";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly StreamingHttpExecutor _streamingExecutor;
        private readonly LlmAuthCircuit _authCircuit;
        private string _model;
        private bool _enableThinking;
        private int? _thinkingBudgetTokens;

        public BrainarrAnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrAnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrAnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Anthropic API key is required", nameof(apiKey));

            _apiKey = apiKey;
            ApplyModel(model ?? BrainarrConstants.DefaultAnthropicModel);
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Anthropic";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // Phase 5b: Streaming flag added now that common ships AnthropicStreamDecoder.
            // StreamAsync still returns null pending the IHttpClient → Stream bridge — same
            // pattern as wave 4a's BrainarrOpenAiProvider / wave 4b cloud providers.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision
                  | LlmCapabilityFlags.ExtendedThinking,
            MaxContextTokens = 200_000,
            UsesOpenAiCompatibleApi = false,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            ApplyModel(modelName);
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var modelRaw = ModelIdMapper.ToRawId("anthropic", _model);
                var probe = new Dictionary<string, object>
                {
                    ["model"] = modelRaw,
                    ["messages"] = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    ["max_tokens"] = 10,
                };
                if (_enableThinking)
                {
                    probe["thinking"] = BuildThinkingNode();
                }

                var response = await SendAsync(probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", modelRaw);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "apiKey",
                    modelRaw,
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

            // Wave-7C auth circuit pre-flight.
            if (_authCircuit.IsOpen(ProviderIdConst, _apiKey, out var circuitReason))
            {
                throw new AuthenticationException(ProviderIdConst, LlmErrorCode.AuthenticationFailed,
                    "Auth circuit open: " + circuitReason);
            }

            var modelRaw = ModelIdMapper.ToRawId(
                "anthropic",
                !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelRaw,
                ["messages"] = new[] { new { role = "user", content = request.Prompt } },
                ["max_tokens"] = request.MaxTokens ?? 2000,
                ["temperature"] = (double?)request.Temperature ?? 0.8,
            };
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                body["system"] = request.SystemPrompt;
            }

            // Phase 5f: explicit request.Thinking takes precedence over legacy sentinel state.
            // When Disabled is set, suppress thinking even if the constructor saw a #thinking
            // sentinel. When Enabled is set, force-emit thinking even if the constructor did not.
            // When Auto or null, fall through to legacy state (_enableThinking from sentinel).
            if (TryResolveThinking(request, out var thinkingNode))
            {
                body["thinking"] = thinkingNode;
            }

            LlmResponse result;
            try
            {
                var response = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    var ex = MapResponseError(response);
                    if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                    {
                        _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, ex);
                        throw ex;
                    }
                    throw ex;
                }

                result = ParseCompletion(response.Content ?? string.Empty);
            }
            catch (AuthenticationException)
            {
                // Already recorded in the status-code branch above — don't double-count.
                throw;
            }
            catch (LlmProviderException lpe) when (
                lpe.ErrorCode == LlmErrorCode.AuthenticationFailed ||
                lpe.ErrorCode == LlmErrorCode.AuthorizationFailed)
            {
                // Auth exceptions from SendAsync (HttpException path).
                _authCircuit.RecordAuthFailure(ProviderIdConst, _apiKey, lpe);
                throw;
            }

            _authCircuit.RecordSuccess(ProviderIdConst, _apiKey);
            return result;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return StreamAsyncCore(request, cancellationToken);
        }

        private async IAsyncEnumerable<LlmStreamChunk> StreamAsyncCore(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var modelRaw = ModelIdMapper.ToRawId(
                "anthropic",
                !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model);

            var body = new Dictionary<string, object>
            {
                ["model"] = modelRaw,
                ["messages"] = new[] { new { role = "user", content = request.Prompt } },
                ["max_tokens"] = request.MaxTokens ?? 2000,
                ["temperature"] = (double?)request.Temperature ?? 0.8,
                ["stream"] = true,
            };
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                body["system"] = request.SystemPrompt!;
            }
            if (TryResolveThinking(request, out var thinkingNode))
            {
                body["thinking"] = thinkingNode;
            }

            var headers = new[]
            {
                new KeyValuePair<string, string>("x-api-key", _apiKey),
                new KeyValuePair<string, string>("anthropic-version", AnthropicVersion),
                new KeyValuePair<string, string>("Accept", "text/event-stream"),
            };

            var stream = await _streamingExecutor.SendForStreamingAsync(
                ProviderIdConst,
                HttpMethod.Post,
                BrainarrConstants.AnthropicMessagesUrl,
                headers,
                JsonConvert.SerializeObject(body),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await using (stream)
            {
                var decoder = new AnthropicStreamDecoder();
                await foreach (var chunk in decoder.DecodeAsync(stream, cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private void ApplyModel(string raw)
        {
            _enableThinking = false;
            _thinkingBudgetTokens = null;
            _model = raw ?? BrainarrConstants.DefaultAnthropicModel;

            if (!_model.Contains("#thinking", StringComparison.Ordinal)) return;

            _enableThinking = true;
            try
            {
                var start = _model.IndexOf("#thinking", StringComparison.Ordinal);
                var open = _model.IndexOf('(', start);
                var close = open > 0 ? _model.IndexOf(')', open + 1) : -1;
                if (open > 0 && close > open)
                {
                    var inside = _model.Substring(open + 1, close - open - 1).Trim();
                    if (inside.StartsWith("tokens=", StringComparison.OrdinalIgnoreCase))
                    {
                        inside = inside.Substring(7).Trim();
                    }
                    if (int.TryParse(inside, out var budget) && budget > 0)
                    {
                        _thinkingBudgetTokens = budget;
                    }
                }
            }
            catch
            {
                // best effort
            }

            _model = _model.Replace("#thinking", string.Empty, StringComparison.Ordinal);
            _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(tokens=\\d+\\)", string.Empty);
            _model = System.Text.RegularExpressions.Regex.Replace(_model, "\\(\\d+\\)", string.Empty);
            _model = _model.Trim();
        }

        private Dictionary<string, object> BuildThinkingNode()
            => BuildThinkingNode(_thinkingBudgetTokens);

        private static Dictionary<string, object> BuildThinkingNode(int? budgetTokens)
        {
            var node = new Dictionary<string, object> { ["type"] = "auto" };
            if (budgetTokens.HasValue && budgetTokens.Value > 0)
            {
                node["budget_tokens"] = budgetTokens.Value;
            }
            return node;
        }

        /// <summary>
        /// Resolves whether to attach a <c>thinking</c> directive to the outgoing payload, taking
        /// the new <see cref="LlmRequest.Thinking"/> hint into account when present and falling
        /// back to the legacy <c>#thinking[(tokens=N)]</c> sentinel state otherwise.
        /// </summary>
        private bool TryResolveThinking(LlmRequest request, out Dictionary<string, object> node)
        {
            // Explicit per-request hint wins.
            if (request.Thinking is { } hint)
            {
                switch (hint.Mode)
                {
                    case LlmThinkingMode.Disabled:
                        node = null!;
                        return false;
                    case LlmThinkingMode.Enabled:
                        node = BuildThinkingNode(hint.BudgetTokens);
                        return true;
                    case LlmThinkingMode.Auto:
                    default:
                        // Fall through to legacy sentinel state.
                        break;
                }
            }

            if (_enableThinking)
            {
                node = BuildThinkingNode();
                return true;
            }

            node = null!;
            return false;
        }

        private async Task<HttpResponse> SendAsync(object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(BrainarrConstants.AnthropicMessagesUrl)
                .SetHeader("x-api-key", _apiKey)
                .SetHeader("anthropic-version", AnthropicVersion)
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
                throw MapResponseError(hex.Response, hex);
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

        /// <summary>
        /// Maps a Lidarr <see cref="HttpResponse"/> error to an <see cref="LlmProviderException"/>,
        /// extracting the <c>Retry-After</c> response header so callers (e.g., adaptive limiters)
        /// can honour it on <see cref="RateLimitException"/>.
        /// </summary>
        /// <remarks>
        /// Phase 5f: previously this site dropped Retry-After by calling the 3-arg
        /// <see cref="LlmErrorMapper.MapHttpError(string, int, string?, Exception?)"/> overload.
        /// Common's new <c>(string, int, string?, TimeSpan?, Exception?)</c> overload now plumbs
        /// the value through to <see cref="LlmProviderException.RetryAfter"/>.
        /// </remarks>
        private static LlmProviderException MapResponseError(HttpResponse response, Exception? inner = null)
        {
            return LlmErrorMapper.MapHttpError(
                ProviderIdConst,
                (int)response.StatusCode,
                Truncate(response.Content),
                BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                inner);
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

                // Concatenate all `content[].text` blocks of type "text".
                var text = parsed?.Content == null
                    ? string.Empty
                    : string.Join(string.Empty, parsed.Content
                        .Where(b => b != null && string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                        .Select(b => b.Text ?? string.Empty));

                var reasoning = parsed?.Content == null
                    ? null
                    : string.Join(string.Empty, parsed.Content
                        .Where(b => b != null && string.Equals(b.Type, "thinking", StringComparison.OrdinalIgnoreCase))
                        .Select(b => b.Thinking ?? string.Empty));

                return new LlmResponse
                {
                    Content = text,
                    ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
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
                        "Invalid Anthropic API key or authentication error. Recreate a key at https://console.anthropic.com and ensure it has API access.",
                        BrainarrConstants.DocsAnthropicSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Anthropic rate limit exceeded. Wait a minute and retry, or switch to Haiku for lower cost.",
                        BrainarrConstants.DocsAnthropicSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Anthropic credits/quota exhausted. Add a payment method or reduce usage: https://console.anthropic.com",
                        BrainarrConstants.DocsAnthropicCreditLimit),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
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

            [JsonProperty("thinking")]
            public string? Thinking { get; set; }
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
