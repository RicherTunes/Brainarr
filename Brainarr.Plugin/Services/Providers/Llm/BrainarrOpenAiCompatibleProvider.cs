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
    /// <see cref="ILlmProvider"/> implementation for any generic OpenAI-compatible
    /// HTTP endpoint — self-hosted llama.cpp servers, vLLM, LocalAI, custom proxies, etc.
    ///
    /// <para>
    /// Wave-4c catch-all provider. Unlike LM Studio and Ollama, this provider has <strong>no
    /// localhost default</strong>: the base URL is required because the user is wiring up an
    /// arbitrary endpoint. Auth is opt-in via an optional <c>Authorization: Bearer</c> token.
    /// </para>
    ///
    /// <para>
    /// Provider-specific quirks captured here:
    /// 1. JsonMode is <strong>opt-in</strong> via <see cref="LlmRequest.ProviderOptions"/>
    ///    rather than always-on — many self-hosted OpenAI-compat impls (older llama.cpp
    ///    server builds in particular) reject <c>response_format: json_object</c> with a 400.
    ///    The capability flag intentionally omits <c>JsonMode</c>; users opt in by setting
    ///    <c>ProviderOptions["json_mode"] = true</c>.
    /// 2. Health check: <c>GET /v1/models</c>. Almost universally supported; if a backend
    ///    rejects it, the user can also exercise the <c>CompleteAsync</c> path which is the
    ///    real liveness signal anyway.
    /// 3. Connection-refused: when the base URL is unreachable, the provider degrades health
    ///    gracefully (no exception cascade) and surfaces a generic "endpoint unreachable"
    ///    hint via <see cref="IBrainarrLlmHintSource"/>. No vendor-specific guidance can be
    ///    given here — users supply their own backend.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiCompatibleProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openai-compatible";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _baseUrl;
        private readonly string? _apiKey;
        private string _model;

        public BrainarrOpenAiCompatibleProvider(
            IHttpClient httpClient,
            Logger logger,
            string baseUrl,
            string model,
            string? apiKey = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Base URL is required for the OpenAI-compatible provider", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model id is required for the OpenAI-compatible provider", nameof(model));

            _baseUrl = baseUrl.TrimEnd('/');
            _model = model;
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "OpenAI-Compatible";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            // Conservative — JsonMode intentionally omitted because not every backend
            // implements `response_format`. Callers opt in via
            // ProviderOptions["json_mode"] = true.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.SystemPrompt,
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
            try
            {
                var builder = new HttpRequestBuilder($"{_baseUrl}/v1/models");
                if (!string.IsNullOrEmpty(_apiKey))
                {
                    builder.SetHeader("Authorization", $"Bearer {_apiKey}");
                }

                var request = builder.Build();
                request.Method = HttpMethod.Get;
                request.SuppressHttpError = true;
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                cancellationToken.ThrowIfCancellationRequested();
                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                sw.Stop();

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, _apiKey != null ? "apiKey" : "none", _model);
                }

                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    _apiKey != null ? "apiKey" : "none",
                    _model,
                    errorCode: ((int)response.StatusCode).ToString());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                sw.Stop();
                return ProviderHealthResult.Unhealthy(
                    $"HTTP {(int)hex.Response.StatusCode}",
                    sw.Elapsed,
                    ProviderIdConst,
                    _apiKey != null ? "apiKey" : "none",
                    _model,
                    errorCode: ((int)hex.Response.StatusCode).ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                return ProviderHealthResult.Unhealthy(
                    $"Cannot reach OpenAI-compatible endpoint at {SafeAuthority(_baseUrl)}: {ex.Message}",
                    sw.Elapsed,
                    ProviderIdConst,
                    _apiKey != null ? "apiKey" : "none",
                    _model,
                    errorCode: "ConnectionFailed");
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
            // Streaming uses common's OpenAiStreamDecoder once a direct HttpClient pipeline
            // lands; today the host's IHttpClient buffers full responses. Match wave 4a/4b.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private object BuildRequestBody(LlmRequest request)
        {
            var temp = (double?)request.Temperature ?? 0.7;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model;

            // Opt-in JSON mode — only emit response_format when the caller asked for it,
            // because many self-hosted backends 400 on the parameter.
            bool jsonMode = false;
            if (request.ProviderOptions != null && request.ProviderOptions.TryGetValue("json_mode", out var jm))
            {
                if (jm is bool b) jsonMode = b;
                else if (jm is string s) bool.TryParse(s, out jsonMode);
            }

            object? responseFormat = jsonMode ? new { type = "json_object" } : null;

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
            var builder = new HttpRequestBuilder($"{_baseUrl}/v1/chat/completions")
                .SetHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(_apiKey))
            {
                builder.SetHeader("Authorization", $"Bearer {_apiKey}");
            }

            var request = builder.Build();
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
                var trimmed = content.TrimStart('﻿');
                var parsed = JsonConvert.DeserializeObject<OpenAiChatCompletionDto>(trimmed);
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

        private static string SafeAuthority(string url)
        {
            try { return new Uri(url).Authority; }
            catch { return "(configured host)"; }
        }

        BrainarrLlmHint? IBrainarrLlmHintSource.GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.ConnectionFailed =>
                    new BrainarrLlmHint(
                        $"Cannot reach the OpenAI-compatible endpoint at {SafeAuthority(_baseUrl)}. Verify the base URL and that the backend is running.",
                        null),
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Authentication failed against the OpenAI-compatible endpoint. If your backend requires a token, set it in the provider configuration.",
                        null),
                LlmErrorCode.InvalidRequest =>
                    new BrainarrLlmHint(
                        "The OpenAI-compatible backend rejected the request. Some self-hosted servers do not implement the full OpenAI schema (e.g. response_format). Try disabling JSON mode if it was enabled.",
                        null),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "The OpenAI-compatible backend does not recognize the requested model id. Check the model name against the backend's /v1/models list.",
                        null),
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
