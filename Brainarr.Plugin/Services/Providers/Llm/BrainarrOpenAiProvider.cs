using System;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Observability;
using Lidarr.Plugin.Common.Providers.OpenAi;
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
    /// Wave-4a foundation provider, rebuilt on <see cref="OpenAiChatProviderBase"/>
    /// (B-201 / #46 dedup). No wire-format quirks beyond the 0.8 default temperature;
    /// auth + endpoint are pinned by <c>CompleteAsync_UsesBearerAuth_AgainstChatCompletionsEndpoint</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiProvider : OpenAiChatProviderBase, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openai";
        private readonly Logger _logger;

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrOpenAiProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(new BrainarrOpenAiChatTransport(httpClient, logger, streamingExecutor), BrainarrOpenAiChatPolicy.RequireApiKey(apiKey, "OpenAI"), model,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultOpenAIModel,
                   completionTimeout: TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout),
                   healthTimeout: TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout),
                   authCircuit: new BrainarrOpenAiChatAuthCircuit(authCircuit ?? new LlmAuthCircuit(logger)))
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public override string DisplayName => "OpenAI";

        protected override TimeSpan ResolveCompletionTimeout() =>
            BrainarrOpenAiChatPolicy.ResolveCompletionTimeout();

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
        protected override Uri ChatCompletionsEndpoint => new(BrainarrConstants.OpenAIChatCompletionsUrl);

        protected override string NormalizeModel(string model) => ModelIdMapper.ToRawId(ProviderIdConst, model);

        protected override IDisposable? BeginCompletionScope()
            => PluginLogContext.Push("Brainarr", "LlmComplete", provider: ProviderIdConst);

        /// <inheritdoc />
        protected override double DefaultTemperature => 0.8;

        /// <inheritdoc />
        protected override void OnCompletionRequestStarting()
        {
            _logger.Debug($"{PluginLogContext.Current?.LinePrefix()}[REQUEST_START] OpenAI completion url={Scrub.Url(BrainarrConstants.OpenAIChatCompletionsUrl)}");
        }

        /// <inheritdoc />
        public BrainarrLlmHint? GetUserHint(LlmProviderException exception)
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
