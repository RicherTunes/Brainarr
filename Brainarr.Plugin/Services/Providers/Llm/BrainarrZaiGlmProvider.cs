using System;
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
    /// <see cref="ILlmProvider"/> implementation for Z.AI (Zhipu) GLM
    /// (<c>https://api.z.ai/api/paas/v4/chat/completions</c>), built on
    /// <see cref="BrainarrOpenAiChatProviderBase"/> (B-201 / #46 dedup).
    ///
    /// <para>
    /// Z.AI's PaaS API speaks the OpenAI Chat Completions wire format with
    /// <c>response_format = {"type":"json_object"}</c> for strict-JSON output.
    /// </para>
    ///
    /// <para>
    /// CONTRACT: this provider SENDS <c>temperature</c> (the base default) — the
    /// OPPOSITE of its sibling <c>BrainarrZaiCodingProvider</c>, whose Anthropic-format
    /// Coding endpoint rejects the parameter with <c>[1210]</c>. Do not unify the two;
    /// guarded by <c>CompleteAsync_SendsTemperature</c> vs <c>CompleteAsync_OmitsTemperature</c>.
    /// </para>
    ///
    /// <para>
    /// Lineup as of May 2026 (verified against docs.z.ai):
    /// </para>
    ///
    /// <list type="bullet">
    /// <item><c>glm-5.1</c> — flagship, 200K context, 128K max output, long-horizon agent</item>
    /// <item><c>glm-5</c> — 745B MoE, released Feb 2026</item>
    /// <item><c>glm-5-turbo</c> — fast/coding variant</item>
    /// <item><c>glm-4.7</c> — multilingual coding gains over 4.6</item>
    /// <item><c>glm-4.6</c> — 200K context, was flagship before 4.7</item>
    /// <item><c>glm-4.5</c> — 355B params, agent-oriented</item>
    /// <item><c>glm-4.5-air</c> — 106B params, balanced cost/quality (default)</item>
    /// <item><c>glm-4-32b-0414-128k</c> — 32B parameter variant, 128K context</item>
    /// </list>
    /// </summary>
    public sealed class BrainarrZaiGlmProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "zaiglm";

        public BrainarrZaiGlmProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrZaiGlmProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrZaiGlmProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultZaiGlmModel,
                   keyOwnerName: "Z.AI")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "Z.AI GLM";

        /// <inheritdoc />
        public override LlmProviderCapabilities Capabilities => new()
        {
            // GLM-5.1 and GLM-4.6 explicitly support 200K context + JSON mode + tool
            // calling per Z.AI docs. Older models (4.5 family) support TextCompletion
            // + Streaming + JsonMode but have shorter contexts. Capabilities are
            // declared at the provider level so we declare the union; per-model
            // limits are enforced server-side.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.JsonMode
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        protected override string ChatCompletionsUrl => BrainarrConstants.ZaiGlmChatCompletionsUrl;

        /// <inheritdoc />
        protected override LlmProviderException MapHttpError(int statusCode, string? body, TimeSpan? retryAfter, Exception? inner)
            => MapZaiHttpError(statusCode, body, retryAfter, inner);

        // Z.AI returns HTTP 429 for two semantically different conditions:
        //   - real rate limiting (transient, retry-after applies)
        //   - "Insufficient balance or no resource package" (code 1113) — the account
        //     simply has no PaaS credits, so retrying without topping up will never
        //     succeed. The default 429 -> RateLimited mapping sends users down a
        //     misleading "wait and retry" path; intercept those codes and surface
        //     QuotaExceeded so the existing top-up hint fires instead.
        // Coding-Plan tokens hitting this endpoint also produce 1113 — see
        // BrainarrZaiCodingProvider for the alternative endpoint that serves them
        // (it shares this mapper, which is why it stays internal static here).
        internal static LlmProviderException MapZaiHttpError(int statusCode, string? body, TimeSpan? retryAfter, Exception? inner)
        {
            if (statusCode == 429 && TryParseZaiErrorCode(body, out var code, out var message))
            {
                if (code == "1113" || code == "1115")
                {
                    var detail = string.IsNullOrWhiteSpace(message)
                        ? "Z.AI account has no PaaS resource package or insufficient balance."
                        : message!;
                    return new ProviderException(ProviderIdConst, LlmErrorCode.QuotaExceeded, detail, inner);
                }
            }

            return LlmErrorMapper.MapHttpError(ProviderIdConst, statusCode, body, retryAfter, inner);
        }

        internal static bool TryParseZaiErrorCode(string? body, out string? code, out string? message)
        {
            code = null;
            message = null;
            if (string.IsNullOrWhiteSpace(body)) return false;

            try
            {
                var env = JsonConvert.DeserializeObject<ZaiErrorEnvelope>(body);
                code = env?.Error?.Code?.ToString();
                message = env?.Error?.Message;
                return !string.IsNullOrEmpty(code);
            }
            catch
            {
                return false;
            }
        }

        private sealed class ZaiErrorEnvelope
        {
            [JsonProperty("error")]
            public ZaiErrorDto? Error { get; set; }
        }

        private sealed class ZaiErrorDto
        {
            [JsonProperty("code")]
            public object? Code { get; set; }

            [JsonProperty("message")]
            public string? Message { get; set; }
        }

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Z.AI API key. Verify your key at https://z.ai/manage-apikey/apikey-list and ensure it is active.",
                        BrainarrConstants.DocsZaiGlmSection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Z.AI rate limit exceeded. Wait a few minutes or reduce request frequency. Free-tier accounts have lower rate limits than paid plans.",
                        BrainarrConstants.DocsZaiGlmSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Z.AI PaaS balance is empty (error 1113). The PaaS endpoint is metered separately from the Coding Plan subscription — a Coding Plan token does NOT grant PaaS credits. Either top up PaaS at https://z.ai/manage-apikey/apikey-list, or switch the Brainarr provider to 'Z.AI Coding Subscription' to use your Coding Plan against the Anthropic-compatible endpoint.",
                        BrainarrConstants.DocsZaiGlmSection),
                _ => null,
            };
        }
    }
}
