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
    /// <see cref="ILlmProvider"/> implementation for Anthropic's Messages API.
    ///
    /// <para>
    /// Wave-4a foundation provider. Supports the <c>thinking</c> extended-reasoning option
    /// via the legacy <c>#thinking[(tokens=N)]</c> sentinel that brainarr's settings layer
    /// already encodes into the model id.
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
        private string _model;
        private bool _enableThinking;
        private int? _thinkingBudgetTokens;

        public BrainarrAnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Anthropic API key is required", nameof(apiKey));

            _apiKey = apiKey;
            ApplyModel(model ?? BrainarrConstants.DefaultAnthropicModel);
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
            if (_enableThinking)
            {
                body["thinking"] = BuildThinkingNode();
            }

            var response = await SendAsync(body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)response.StatusCode,
                    Truncate(response.Content));
            }

            return ParseCompletion(response.Content ?? string.Empty);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            // Phase 5b: common's AnthropicStreamDecoder is ready to consume the Anthropic
            // SSE wire format (message_start, content_block_delta(text|thinking),
            // message_delta, message_stop). Wiring is gated on the host-IHttpClient →
            // System.IO.Stream bridge that wave 4a/4b providers also defer; once that
            // lands, this becomes:
            //
            //   var stream = await GetResponseStreamAsync(request, cancellationToken).ConfigureAwait(false);
            //   return new AnthropicStreamDecoder().DecodeAsync(stream, cancellationToken);
            //
            // Until then, return null and rely on CompleteAsync. Capability flag is set so
            // callers can detect that streaming is supported once the bridge is wired.
            return null;
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
        {
            var node = new Dictionary<string, object> { ["type"] = "auto" };
            if (_thinkingBudgetTokens.HasValue && _thinkingBudgetTokens.Value > 0)
            {
                node["budget_tokens"] = _thinkingBudgetTokens.Value;
            }
            return node;
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
                throw LlmErrorMapper.MapHttpError(
                    ProviderIdConst,
                    (int)hex.Response.StatusCode,
                    Truncate(hex.Response.Content),
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
