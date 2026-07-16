using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <see cref="ILlmProvider"/> implementation for Perplexity
    /// (<c>https://api.perplexity.ai/chat/completions</c>), built on
    /// <see cref="BrainarrOpenAiChatProviderBase"/> (B-201 / #46 dedup).
    ///
    /// <para>
    /// Provider-specific quirks:
    /// 1. Citations: their online Sonar models surface a <c>citations</c> array
    ///    (top-level or per-choice) listing source URLs that grounded the response —
    ///    surfaced via <see cref="LlmResponse.Metadata"/> under the key <c>"citations"</c>
    ///    as a <c>List&lt;string&gt;</c>.
    /// 2. Citation markers like <c>[1]</c>, <c>[12]</c> sometimes appear inline in the
    ///    response content. Stripping is left to <c>RecommendationJsonParser</c>
    ///    (music-domain), but we strip them defensively here (both in
    ///    <see cref="ParseCompletion"/> and per streamed chunk) so other consumers
    ///    receive clean output.
    /// 3. JSON-mode: not formally supported across all routes —
    ///    <see cref="SupportsJsonResponseFormat"/> is false so <c>response_format</c> is
    ///    never emitted, and the capability flag is omitted.
    /// 4. The completion request additionally pins <c>Accept: application/json</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrPerplexityProvider : BrainarrOpenAiChatProviderBase
    {
        private const string ProviderIdConst = "perplexity";
        private const string ApiUrl = "https://api.perplexity.ai/chat/completions";

        private static readonly Regex CitationMarkerRegex =
            new("\\[\\d{1,3}\\]", RegexOptions.Compiled);

        public BrainarrPerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : this(httpClient, logger, apiKey, model, streamingExecutor: null, authCircuit: null)
        {
        }

        public BrainarrPerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor)
            : this(httpClient, logger, apiKey, model, streamingExecutor, authCircuit: null)
        {
        }

        public BrainarrPerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string? model, StreamingHttpExecutor? streamingExecutor, LlmAuthCircuit? authCircuit)
            : base(httpClient, logger, apiKey, model, streamingExecutor, authCircuit,
                   providerId: ProviderIdConst,
                   defaultModel: BrainarrConstants.DefaultPerplexityModel,
                   keyOwnerName: "Perplexity")
        {
        }

        /// <inheritdoc />
        public override string DisplayName => "Perplexity";

        /// <inheritdoc />
        public override LlmProviderCapabilities Capabilities => new()
        {
            // JsonMode intentionally omitted: Perplexity's response_format support varies
            // across Sonar variants.
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.Streaming
                  | LlmCapabilityFlags.SystemPrompt,
            UsesOpenAiCompatibleApi = true,
        };

        /// <inheritdoc />
        protected override string ChatCompletionsUrl => ApiUrl;

        /// <inheritdoc />
        protected override bool SupportsJsonResponseFormat => false;

        /// <inheritdoc />
        protected override void AddCompletionRequestHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Accept", "application/json");
        }

        /// <inheritdoc />
        protected override object BuildHealthProbeBody()
        {
            return new
            {
                model = CurrentModel,
                messages = new[] { new { role = "user", content = "Reply with 'OK'" } },
                max_tokens = 10,
            };
        }

        /// <inheritdoc />
        protected override LlmStreamChunk TransformStreamChunk(LlmStreamChunk chunk)
        {
            // Defensively strip inline citation markers like [1], [12] from streamed deltas
            // so consumers see clean output, matching CompleteAsync's behavior.
            if (chunk.ContentDelta is { Length: > 0 } delta)
            {
                var stripped = CitationMarkerRegex.Replace(delta, string.Empty);
                if (!ReferenceEquals(stripped, delta))
                {
                    return chunk with { ContentDelta = stripped };
                }
            }
            return chunk;
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
                var parsed = JsonConvert.DeserializeObject<PerplexityChatCompletionDto>(content);
                var choice = parsed?.Choices?.FirstOrDefault();
                var rawText = choice?.Message?.Content ?? string.Empty;

                // Defensively strip inline citation markers like [1], [12]. RecommendationJsonParser
                // does this too, but other consumers (logging, future direct callers) benefit from
                // a clean Content field here.
                var text = string.IsNullOrEmpty(rawText)
                    ? rawText
                    : CitationMarkerRegex.Replace(rawText, string.Empty);

                // Surface citations via Metadata. Perplexity may report them at top-level
                // (older shape) or per-choice (newer shape) — we merge both, deduped.
                var citations = MergeCitations(parsed?.Citations, choice?.Citations);

                IReadOnlyDictionary<string, object>? metadata = null;
                if (citations.Count > 0)
                {
                    metadata = new Dictionary<string, object> { ["citations"] = citations };
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

        private static List<string> MergeCitations(List<string>? top, List<string>? perChoice)
        {
            var merged = new List<string>();
            if (top != null) merged.AddRange(top.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (perChoice != null) merged.AddRange(perChoice.Where(s => !string.IsNullOrWhiteSpace(s)));
            return merged.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <inheritdoc />
        protected override BrainarrLlmHint? GetUserHint(LlmProviderException exception)
        {
            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Invalid Perplexity API key. Verify your key at https://www.perplexity.ai/settings/api and ensure it is active.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Perplexity rate limit exceeded. Wait a few minutes or reduce request frequency.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Perplexity quota exhausted. Check your subscription at https://www.perplexity.ai/settings/api.",
                        BrainarrConstants.DocsPerplexitySection),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        "Perplexity model not found. Verify the model id (e.g., 'sonar-pro', 'llama-3.1-sonar-large-128k-online').",
                        BrainarrConstants.DocsPerplexitySection),
                _ => null,
            };
        }

        // -- DTOs -------------------------------------------------------------
        private sealed class PerplexityChatCompletionDto
        {
            [JsonProperty("choices")]
            public List<PerplexityChoiceDto>? Choices { get; set; }

            [JsonProperty("usage")]
            public PerplexityUsageDto? Usage { get; set; }

            // Top-level citations (older Sonar response shape).
            [JsonProperty("citations")]
            public List<string>? Citations { get; set; }
        }

        private sealed class PerplexityChoiceDto
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public PerplexityMessageDto? Message { get; set; }

            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }

            // Per-choice citations (newer shape).
            [JsonProperty("citations")]
            public List<string>? Citations { get; set; }
        }

        private sealed class PerplexityMessageDto
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }
        }

        private sealed class PerplexityUsageDto
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
