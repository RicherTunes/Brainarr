using System.Collections.Generic;
using System.Linq;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for OpenRouter
    /// (<c>https://openrouter.ai/api/v1/chat/completions</c>), built on
    /// <see cref="BrainarrOpenAiChatProviderBase"/> (B-201 / #46 dedup).
    ///
    /// <para>
    /// Provider-specific quirks captured here:
    /// 1. <c>HTTP-Referer</c> and <c>X-Title</c> identifying headers — OpenRouter uses these
    ///    to attribute requests on its dashboard. Brainarr supplies its GitHub URL + project
    ///    name from <see cref="BrainarrConstants"/>. Applied to BOTH the completion and the
    ///    streaming request.
    /// 2. Model ids are typically vendor-prefixed (<c>anthropic/claude-3.5-sonnet</c>); the
    ///    legacy mapper passes those through unchanged.
    /// 3. The health probe uses the fixed cheap test model
    ///    (<see cref="BrainarrConstants.DefaultOpenRouterTestModelRaw"/>), not the configured
    ///    model — probing an expensive route on every health check would bill the user.
    /// 4. <see cref="ParseCompletion"/> surfaces the actually-routed model (top-level
    ///    <c>model</c> field) via <c>Metadata["routed_model"]</c> for observability when the
    ///    client requested <c>openrouter/auto</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenRouterProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "openrouter";

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrOpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultOpenRouterModel,
                   keyOwnerName: "OpenRouter")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "OpenRouter";

        /// <inheritdoc />
        public override LlmProviderCapabilities Capabilities => new()
        {
            // OpenRouter exposes an OpenAI-compatible surface; the exact features available
            // depend on the upstream model OpenRouter routes the request to. JsonMode is
            // listed because the gateway accepts the flag for compatible models and silently
            // ignores it for others — matching legacy behavior.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        protected override string ChatCompletionsUrl => BrainarrConstants.OpenRouterChatCompletionsUrl;

        /// <inheritdoc />
        protected override double DefaultTemperature => 0.8;

        /// <inheritdoc />
        protected override void AddCompletionRequestHeaders(HttpRequestBuilder builder)
        {
            builder
                .SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer)
                .SetHeader("X-Title", BrainarrConstants.OpenRouterTitle);
        }

        /// <inheritdoc />
        protected override IReadOnlyList<KeyValuePair<string, string>> BuildStreamingHeaders()
        {
            var headers = new List<KeyValuePair<string, string>>(base.BuildStreamingHeaders())
            {
                new KeyValuePair<string, string>("HTTP-Referer", BrainarrConstants.ProjectReferer),
                new KeyValuePair<string, string>("X-Title", BrainarrConstants.OpenRouterTitle),
            };
            return headers;
        }

        /// <inheritdoc />
        protected override object BuildHealthProbeBody()
        {
            // Probe the fixed cheap test model, NOT the configured one — health checks
            // must not bill the user's premium route.
            return new
            {
                model = BrainarrConstants.DefaultOpenRouterTestModelRaw,
                messages = new[] { new { role = "user", content = "Reply with OK" } },
                max_tokens = 5,
            };
        }

        /// <inheritdoc />
        protected override LlmResponse ParseCompletion(string content)
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

                IReadOnlyDictionary<string, object>? metadata = null;
                // OpenRouter surfaces the actually-routed model in `model` — useful for
                // observability when the client requested `openrouter/auto` and got a
                // specific upstream.
                if (!string.IsNullOrWhiteSpace(parsed?.Model))
                {
                    metadata = new Dictionary<string, object> { ["routed_model"] = parsed!.Model! };
                }

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
                    Metadata = metadata,
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid OpenRouter API key. Ensure it starts with 'sk-or-' and is active: https://openrouter.ai/keys",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenRouter rate limit exceeded. Wait, reduce request frequency, or choose a cheaper/faster route.",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenRouter requires payment/credit. Add credit or resolve billing: https://openrouter.ai/settings/billing",
                        BrainarrConstants.DocsOpenRouterSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "OpenRouter model not found. Verify the model id at https://openrouter.ai/models — many ids are vendor-prefixed (e.g., 'anthropic/claude-3.5-sonnet').",
                        BrainarrConstants.DocsOpenRouterSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class OpenAiChatCompletionDto
        {
            [JsonProperty("model")]
            public string? Model { get; set; }

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
