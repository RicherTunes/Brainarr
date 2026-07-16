using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Observability;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for OpenAI Chat Completions.
    ///
    /// <para>
    /// Wave-4a foundation provider, rebuilt on <see cref="BrainarrOpenAiChatProviderBase"/>
    /// (B-201 / #46 dedup). No wire-format quirks beyond the 0.8 default temperature;
    /// auth + endpoint are pinned by <c>CompleteAsync_UsesBearerAuth_AgainstChatCompletionsEndpoint</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "openai";

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultOpenAIModel,
                   keyOwnerName: "OpenAI")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "OpenAI";

        /// <inheritdoc />
        public override LlmProviderCapabilities Capabilities => new()
        {
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        protected override string ChatCompletionsUrl => BrainarrConstants.OpenAIChatCompletionsUrl;

        /// <inheritdoc />
        protected override double DefaultTemperature => 0.8;

        /// <inheritdoc />
        protected override void OnCompletionRequestStarting()
        {
            ProviderLogger.Debug($"{PluginLogContext.Current?.LinePrefix()}[REQUEST_START] OpenAI completion url={Scrub.Url(BrainarrConstants.OpenAIChatCompletionsUrl)}");
        }

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid OpenAI API key. Ensure it starts with 'sk-' and is active. Recreate at https://platform.openai.com/api-keys and verify billing if required.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenAI rate limit exceeded. Wait 1–5 minutes, reduce request frequency, or switch to a cheaper model.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenAI quota/credits exhausted. Add payment method or reduce usage.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                _ => null,
            };
        }
    }
}
