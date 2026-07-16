using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> implementation for Groq
    /// (<c>https://api.groq.com/openai/v1/chat/completions</c>), built on
    /// <see cref="BrainarrOpenAiChatProviderBase"/> (B-201 / #46 dedup).
    ///
    /// <para>
    /// Provider-specific quirks:
    /// 1. Standard OpenAI shape — no extra headers needed.
    /// 2. <c>JsonMode</c> is supported on a subset of models (base default emits
    ///    <c>response_format = {"type":"json_object"}</c>). Groq returns 422 on routes
    ///    that don't support it; callers that target older Llama-3 routes should leave
    ///    <see cref="LlmRequest.JsonMode"/> at its default (false) and rely on
    ///    system-prompt JSON shaping.
    /// 3. Vision is supported on the Llama-3.2 vision preview models. The capability flag
    ///    is set conservatively (true) — non-vision models simply ignore image parts.
    /// 4. Health probe pins <c>temperature = 0</c> (deterministic cheap probe).
    /// </para>
    /// </summary>
    public sealed class BrainarrGroqProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "groq";

        public BrainarrGroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrGroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrGroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultGroqModel,
                   keyOwnerName: "Groq")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "Groq";

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
        protected override string ChatCompletionsUrl => BrainarrConstants.GroqChatCompletionsUrl;

        /// <inheritdoc />
        protected override object BuildHealthProbeBody()
        {
            // Groq's probe additionally pins temperature=0 (deterministic, cheapest route).
            return new
            {
                model = CurrentModel,
                messages = new[] { new { role = "user", content = "Reply with OK" } },
                max_tokens = 5,
                temperature = 0,
            };
        }

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Groq API key. Verify your key at https://console.groq.com/keys and ensure it is active.",
                        BrainarrConstants.DocsGroqSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Groq rate limit exceeded. Wait a few minutes or reduce request frequency.",
                        BrainarrConstants.DocsGroqSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Groq quota exhausted. Check your usage limits at https://console.groq.com.",
                        BrainarrConstants.DocsGroqSection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "Groq model not found. Verify the model id at https://console.groq.com/docs/models.",
                        BrainarrConstants.DocsGroqSection),
                _ => null,
            };
        }
    }
}
