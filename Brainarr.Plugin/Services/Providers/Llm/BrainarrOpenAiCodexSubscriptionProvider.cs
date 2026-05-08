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
    /// <see cref="ILlmProvider"/> implementation for the OpenAI Chat Completions API authenticated
    /// via OpenAI Codex CLI subscription tokens.
    ///
    /// <para>
    /// Wave-4d migration of <c>OpenAICodexSubscriptionProvider</c>. Tokens come from
    /// <see cref="SubscriptionCredentialLoader.LoadCodexCredentials"/>, which reads
    /// <c>~/.codex/auth.json</c> (or accepts a direct <c>OPENAI_API_KEY</c> override in the same
    /// file for legacy setups). The key/token is sent as a bearer header to the public
    /// Chat Completions endpoint — same wire format as <see cref="BrainarrOpenAiProvider"/>,
    /// different auth source.
    /// </para>
    ///
    /// <para>
    /// Unlike the API-key path, ChatGPT Plus/Team/Pro subscribers can use this provider
    /// without provisioning a separate billable API key — the Codex CLI's OAuth token already
    /// covers the request, scoped to whatever models the subscription tier exposes.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiCodexSubscriptionProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openai-codex-subscription";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _credentialsPath;
        private string _model;
        private string? _credentialError;

        public BrainarrOpenAiCodexSubscriptionProvider(
            IHttpClient httpClient,
            Logger logger,
            string? credentialsPath = null,
            string? model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _credentialsPath = credentialsPath ?? SubscriptionCredentialLoader.GetDefaultCodexPath();
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultOpenAICodexModel : model;

            var probe = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            if (!probe.IsSuccess)
            {
                _credentialError = probe.ErrorMessage;
                _logger.Warn($"OpenAI Codex subscription credentials not loaded: {probe.ErrorMessage}");
            }
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "OpenAI Codex (Subscription)";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision,
            // Streaming intentionally unset; the host's IHttpClient buffers full responses.
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = modelName;
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var token = LoadToken(out var hint);
            if (token == null)
            {
                sw.Stop();
                return ProviderHealthResult.Unhealthy(
                    hint ?? "OpenAI Codex credentials not available",
                    sw.Elapsed,
                    ProviderIdConst,
                    "subscription",
                    _model,
                    errorCode: "CredentialsMissing");
            }

            try
            {
                var probe = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = "Reply with OK" } },
                    max_tokens = 5,
                };

                var response = await SendAsync(token, probe, useTestTimeout: true, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "subscription", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    "subscription",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(
                    lpe.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "subscription",
                    _model,
                    errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(
                    ex.Message,
                    sw.Elapsed,
                    ProviderIdConst,
                    "subscription",
                    _model);
            }
        }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var token = LoadToken(out var hint);
            if (token == null)
            {
                throw new AuthenticationException(
                    ProviderIdConst,
                    LlmErrorCode.AuthenticationFailed,
                    hint ?? "OpenAI Codex credentials not available");
            }

            var body = BuildRequestBody(request);
            var response = await SendAsync(token, body, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

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
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private string? LoadToken(out string? hint)
        {
            var result = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _credentialError = result.ErrorMessage;
                hint = result.ErrorMessage;
                _logger.Warn($"OpenAI Codex subscription token not available: {result.ErrorMessage}");
                return null;
            }

            _credentialError = null;
            hint = null;
            return result.Token;
        }

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.8;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model;

            if (!string.IsNullOrEmpty(request.SystemPrompt))
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

        private async Task<HttpResponse> SendAsync(string token, object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(BrainarrConstants.OpenAIChatCompletionsUrl)
                .SetHeader("Authorization", $"Bearer {token}")
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
                var parsed = JsonConvert.DeserializeObject<OpenAiChatCompletionDto>(content);
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

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            if (!string.IsNullOrEmpty(_credentialError))
            {
                return new BrainarrLlmHint(_credentialError, BrainarrConstants.DocsOpenAIInvalidKey);
            }

            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "OpenAI Codex subscription token rejected. The OAuth token may have expired or been revoked. Run 'codex auth login' to refresh.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenAI subscription quota exhausted. Check your subscription status.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenAI Codex rate limit exceeded. Wait a moment and retry.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        $"Model '{_model}' not available with your subscription. Try 'gpt-4o' or 'gpt-4o-mini'.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class OpenAiChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<OpenAiChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public OpenAiUsageDto? Usage { get; set; }
        }

        private sealed class OpenAiChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public OpenAiMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private sealed class OpenAiMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private sealed class OpenAiUsageDto
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
