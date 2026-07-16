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
    /// <see cref="ILlmProvider"/> implementation for DeepSeek
    /// (<c>https://api.deepseek.com/chat/completions</c>), built on
    /// <see cref="BrainarrOpenAiChatProviderBase"/> (B-201 / #46 dedup).
    ///
    /// <para>
    /// Provider-specific quirks:
    /// 1. <c>deepseek-reasoner</c> emits separate "reasoning" content via the
    ///    <c>reasoning_content</c> field on the message — the OpenAI-style "thinking"
    ///    pattern. <see cref="ParseCompletion"/> surfaces it through
    ///    <see cref="LlmResponse.ReasoningContent"/> so callers can choose whether to
    ///    display or persist it.
    /// 2. <c>JsonMode</c> is supported on all current models (base default emits
    ///    <c>response_format = {"type":"json_object"}</c> when requested).
    /// </para>
    /// </summary>
    public sealed class BrainarrDeepSeekProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "deepseek";

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrDeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultDeepSeekModel,
                   keyOwnerName: "DeepSeek")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "DeepSeek";

        /// <inheritdoc />
        public override LlmProviderCapabilities Capabilities => new()
        {
            // ExtendedThinking flag set: deepseek-reasoner emits a separate reasoning field.
            // Other DeepSeek models (chat, coder) ignore it gracefully — the response simply
            // won't populate ReasoningContent.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.ExtendedThinking,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        protected override string ChatCompletionsUrl => BrainarrConstants.DeepSeekChatCompletionsUrl;

        /// <inheritdoc />
        protected override LlmResponse ParseCompletion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return new LlmResponse { Content = string.Empty };
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<DeepSeekChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content ?? string.Empty;
                var reasoning = choice?.Message?.ReasoningContent;

                return new LlmResponse
                {
                    Content = text,
                    ReasoningContent = string.IsNullOrEmpty(reasoning) ? null : reasoning,
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

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid DeepSeek API key. Verify your key at https://platform.deepseek.com/api_keys and ensure it is active.",
                        BrainarrConstants.DocsDeepSeekSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "DeepSeek rate limit exceeded. Wait a few minutes or reduce request frequency.",
                        BrainarrConstants.DocsDeepSeekSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "DeepSeek credits exhausted. Top up your balance at https://platform.deepseek.com.",
                        BrainarrConstants.DocsDeepSeekSection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class DeepSeekChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<DeepSeekChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public DeepSeekUsageDto? Usage { get; set; }
        }

        private sealed class DeepSeekChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public DeepSeekMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        private sealed class DeepSeekMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }

            // deepseek-reasoner field. Maps to LlmResponse.ReasoningContent.
            [JsonProperty("reasoning_content")]
            public string? ReasoningContent { get; set; }
        }

        private sealed class DeepSeekUsageDto
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
