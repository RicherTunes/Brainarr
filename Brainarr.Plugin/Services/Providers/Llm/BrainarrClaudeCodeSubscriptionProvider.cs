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
    /// <see cref="ILlmProvider"/> implementation for Anthropic's Messages API authenticated
    /// via Claude Code subscription OAuth tokens.
    ///
    /// <para>
    /// Wave-4d migration of <c>ClaudeCodeSubscriptionProvider</c>. Token sourcing is via
    /// <see cref="SubscriptionCredentialLoader.LoadClaudeCodeCredentials"/>, which reads the
    /// CLI-managed credential file at <c>~/.claude/.credentials.json</c>. This is intentionally
    /// kept separate from common's <c>ClaudeCodeProvider</c> (which shells out to the Claude
    /// CLI via <c>CliRunner</c>): users on a Claude Pro/Max subscription can speak to the
    /// Anthropic REST API directly using the same OAuth token the CLI obtained for them, with
    /// no CLI install required at runtime.
    /// </para>
    ///
    /// <para>
    /// Tokens are reloaded on every request — refresh-token rotation done by the CLI in the
    /// background propagates to the next call without restarting the host.
    /// </para>
    ///
    /// <para>
    /// On 401 the loader hint is replaced with one that points the user back at <c>claude
    /// login</c>; this is the meaningful difference vs. <see cref="BrainarrAnthropicProvider"/>,
    /// which advises an API-key rotation.
    /// </para>
    /// </summary>
    public sealed class BrainarrClaudeCodeSubscriptionProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "claude-code-subscription";
        private const string AnthropicVersion = "2023-06-01";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _credentialsPath;
        private string _model;
        private string? _credentialError;

        public BrainarrClaudeCodeSubscriptionProvider(
            IHttpClient httpClient,
            Logger logger,
            string? credentialsPath = null,
            string? model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _credentialsPath = credentialsPath ?? SubscriptionCredentialLoader.GetDefaultClaudeCodePath();
            _model = string.IsNullOrWhiteSpace(model) ? BrainarrConstants.DefaultClaudeCodeModel : model;

            // Probe credentials at construction time so registry/health-check failures surface
            // the correct hint immediately, but don't throw — credential errors are recoverable
            // (the user runs `claude login` and the next request reloads the file).
            var probe = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(_credentialsPath);
            if (!probe.IsSuccess)
            {
                _credentialError = probe.ErrorMessage;
                _logger.Warn($"Claude Code subscription credentials not loaded: {probe.ErrorMessage}");
            }
        }

        /// <inheritdoc />
        public string ProviderId => ProviderIdConst;

        /// <inheritdoc />
        public string DisplayName => "Claude Code (Subscription)";

        /// <inheritdoc />
        public LlmProviderCapabilities Capabilities => new()
        {
            Flags = LlmCapabilityFlags.TextCompletion
                  | LlmCapabilityFlags.SystemPrompt
                  | LlmCapabilityFlags.ToolCalling
                  | LlmCapabilityFlags.Vision,
            // Streaming intentionally unset — same Anthropic-SSE-not-yet-in-common gap as
            // BrainarrAnthropicProvider. Extended thinking is supported by the underlying
            // models, but the subscription provider does not expose the `#thinking` sentinel
            // path (the legacy implementation never did either).
            MaxContextTokens = 200_000,
            UsesOpenAiCompatibleApi = false,
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
                    hint ?? "Claude Code credentials not available",
                    sw.Elapsed,
                    ProviderIdConst,
                    "subscription",
                    _model,
                    errorCode: "CredentialsMissing");
            }

            try
            {
                var probe = new Dictionary<string, object>
                {
                    ["model"] = _model,
                    ["messages"] = new[] { new { role = "user", content = "Reply with 'OK'" } },
                    ["max_tokens"] = 10,
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
                    hint ?? "Claude Code credentials not available");
            }

            var modelRaw = !string.IsNullOrWhiteSpace(request.Model) ? request.Model : _model;
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
            // Streaming reuses the same Anthropic SSE format; not yet decoded by common.
            return null;
        }

        // ---------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------

        private string? LoadToken(out string? hint)
        {
            // Always reload — the CLI rotates tokens in the background.
            var result = SubscriptionCredentialLoader.LoadClaudeCodeCredentials(_credentialsPath);
            if (!result.IsSuccess)
            {
                _credentialError = result.ErrorMessage;
                hint = result.ErrorMessage;
                _logger.Warn($"Claude Code subscription token not available: {result.ErrorMessage}");
                return null;
            }

            _credentialError = null;
            hint = null;
            return result.Token;
        }

        private async Task<HttpResponse> SendAsync(string token, object body, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var request = new HttpRequestBuilder(BrainarrConstants.AnthropicMessagesUrl)
                .SetHeader("x-api-key", token)
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

                var text = parsed?.Content == null
                    ? string.Empty
                    : string.Join(string.Empty, parsed.Content
                        .Where(b => b != null && string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
                        .Select(b => b.Text ?? string.Empty));

                return new LlmResponse
                {
                    Content = text,
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
            // Prefer credential-loader's specific message when we have one (covers expired tokens,
            // missing files, malformed JSON — strictly more informative than the HTTP status).
            if (!string.IsNullOrEmpty(_credentialError))
            {
                return new BrainarrLlmHint(_credentialError, BrainarrConstants.DocsAnthropicSection);
            }

            return exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed =>
                    new BrainarrLlmHint(
                        "Claude Code subscription token rejected. The OAuth token may have expired or been revoked. Run 'claude login' to refresh.",
                        BrainarrConstants.DocsAnthropicSection),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "Claude subscription quota exhausted. Check your subscription status at https://console.anthropic.com.",
                        BrainarrConstants.DocsAnthropicCreditLimit),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "Claude Code rate limit exceeded. Wait a moment and retry.",
                        BrainarrConstants.DocsAnthropicSection),
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
