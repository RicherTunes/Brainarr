using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Lidarr.Plugin.Common.Observability;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// <see cref="ILlmProvider"/> for OpenAI Codex authenticated via the ChatGPT subscription
    /// (the tokens the Codex CLI stores in <c>~/.codex/auth.json</c>).
    ///
    /// <para>
    /// Two auth modes are supported, chosen from the credential file
    /// (<see cref="SubscriptionCredentialLoader.LoadCodexCredentials"/>):
    /// </para>
    /// <list type="bullet">
    ///   <item><b>chatgpt</b> (default for a subscription login): the OAuth <c>access_token</c> is
    ///   NOT accepted by the public Platform <c>chat/completions</c> API. Instead this provider POSTs
    ///   to the ChatGPT backend Responses API (<see cref="BrainarrConstants.OpenAICodexResponsesUrl"/>)
    ///   with <c>Authorization: Bearer</c> + <c>chatgpt-account-id</c> + <c>OpenAI-Beta</c> +
    ///   <c>originator</c> headers — exactly what the Codex CLI sends (live-confirmed 2026-08). The
    ///   backend streams SSE; the buffered stream is reconstructed by <see cref="CodexSseParser"/>.
    ///   When the access token is expired/rejected we refresh it via
    ///   <see cref="CodexTokenRefresher"/> (OAuth2 refresh-token grant, written back to auth.json)
    ///   and retry once.</item>
    ///   <item><b>apikey</b>: if the file carries a raw <c>OPENAI_API_KEY</c>, we fall back to the
    ///   standard Platform <c>chat/completions</c> path — same wire format as the OpenAI provider.</item>
    /// </list>
    ///
    /// <para>
    /// The chatgpt path uses a raw <see cref="HttpClient"/> (not Lidarr's IHttpClient) because the
    /// backend requires a non-Lidarr <c>User-Agent</c>/<c>originator</c>, which the host's
    /// ManagedHttpDispatcher forbids — same rationale and pattern as <c>BrainarrZaiCodingProvider</c>.
    /// </para>
    /// </summary>
    public sealed class BrainarrOpenAiCodexSubscriptionProvider : ILlmProvider, IBrainarrLlmHintSource, IBrainarrLlmModelMutable
    {
        private const string ProviderIdConst = "openai-codex-subscription";

        // Shared raw client for the ChatGPT backend. Per-request timeout is enforced with a linked
        // CancellationTokenSource, so the client timeout is infinite (mirrors BrainarrZaiCodingProvider).
        private static readonly Lazy<System.Net.Http.HttpClient> SharedRawClient = new(static () =>
            new System.Net.Http.HttpClient(new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            });

        private readonly IHttpClient _httpClient;
        private readonly System.Net.Http.HttpClient _rawClient;
        private readonly Logger _logger;
        private readonly string _credentialsPath;
        private readonly LlmAuthCircuit _authCircuit;
        private readonly string _userAgent;
        // Test seam: overrides the live OAuth refresh so provider tests never touch the network.
        private readonly Func<CancellationToken, Task<CodexRefreshResult>>? _refreshOverride;
        private string _model;
        private string? _credentialError;

        public BrainarrOpenAiCodexSubscriptionProvider(
            IHttpClient httpClient,
            Logger logger,
            string? credentialsPath = null,
            string? model = null)
            : this(httpClient, logger, credentialsPath, model, authCircuit: null)
        {
        }

        public BrainarrOpenAiCodexSubscriptionProvider(
            IHttpClient httpClient,
            Logger logger,
            string? credentialsPath,
            string? model,
            LlmAuthCircuit? authCircuit)
            : this(httpClient, logger, credentialsPath, model, authCircuit, rawHandler: null, refreshOverride: null)
        {
        }

        // Test seam: inject a fake HttpMessageHandler for the ChatGPT-backend calls and a refresh
        // override, so tests exercise the SSE/error paths without hitting the network.
        internal BrainarrOpenAiCodexSubscriptionProvider(
            IHttpClient httpClient,
            Logger logger,
            string? credentialsPath,
            string? model,
            LlmAuthCircuit? authCircuit,
            HttpMessageHandler? rawHandler,
            Func<CancellationToken, Task<CodexRefreshResult>>? refreshOverride)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _credentialsPath = credentialsPath ?? SubscriptionCredentialLoader.GetDefaultCodexPath();
            // Stored raw; resolved per call against the credential's auth mode — the ChatGPT
            // backend and the Platform API accept different model-id sets.
            _model = model?.Trim() ?? string.Empty;
            _authCircuit = authCircuit ?? new LlmAuthCircuit(logger);
            _userAgent = $"{BrainarrConstants.OpenAICodexOriginator}/{BrainarrConstants.OpenAICodexClientVersion}";
            _refreshOverride = refreshOverride;
            _rawClient = rawHandler != null
                ? new System.Net.Http.HttpClient(rawHandler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan }
                : SharedRawClient.Value;

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
                  | LlmCapabilityFlags.SystemPrompt,
            // JsonMode intentionally NOT advertised. The Responses API does expose
            // text.format={type:json_object}, but this backend rejects it with 400 unless the *input
            // message* itself contains the word "json" (live-confirmed 2026-08) — a prompt-dependent
            // hard failure we can't guarantee from here. Advertising the flag while dropping it on the
            // wire is worse: LlmProviderAdapter would set request.JsonMode=true and the pipeline would
            // believe strict JSON is enforced when nothing enforces it. Without the flag the shared
            // system-prompt JSON shaping applies, which already yields a clean array from these models.
            // Streaming intentionally unset: the backend streams SSE, but we buffer and reconstruct
            // the full message rather than surfacing chunks (see StreamAsync).
            UsesOpenAiCompatibleApi = false,
        };

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName)) return;
            _model = modelName.Trim();
        }

        // The ChatGPT-backend Codex endpoint only accepts the slugs in
        // BrainarrConstants.OpenAICodexModels; anything else (Platform ids like gpt-4o/o3, bare
        // gpt-5.6, the Pro-only spark variant, or the soon-to-be-removed gpt-5.4 family) is rejected
        // with 400 "model is not supported when using Codex with a ChatGPT account".
        //
        // Coercion is load-bearing for the settings UI, not just defensive: when the user switches to
        // this provider, Lidarr does NOT refetch the schema on a provider-dropdown change, so the model
        // field still holds the previous provider's value (e.g. "GPT41_Mini"). Sending that 400s, which
        // fails the connection Test — and Lidarr refuses to save an import list whose Test failed, so
        // the user can never save Codex to get the refreshed dropdown. Coercing to the default lets the
        // Test pass on first save; the correct dropdown appears on reopen.
        //
        // Matched against the known-good set rather than a "gpt-5" prefix: a prefix test admits
        // gpt-5.4/gpt-5.4-mini, which start 400-ing when they leave Codex on 2026-08-31.
        private string NormalizeCodexModel(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return BrainarrConstants.DefaultOpenAICodexModel;

            foreach (var known in BrainarrConstants.OpenAICodexModels)
            {
                if (string.Equals(model, known, StringComparison.OrdinalIgnoreCase)) return known;
            }

            // Log the substitution: silently discarding an explicit pick (a stale dropdown value, a
            // ManualModelId override, or a newer slug we don't know yet) is otherwise invisible in
            // support logs and reads as "my model choice is ignored at random".
            _logger.Warn(
                $"OpenAI Codex: model '{model}' is not accepted by the ChatGPT backend; using '{BrainarrConstants.DefaultOpenAICodexModel}' instead. " +
                $"Pick one of: {string.Join(", ", BrainarrConstants.OpenAICodexModels)}.");
            return BrainarrConstants.DefaultOpenAICodexModel;
        }

        /// <summary>
        /// Resolves the wire model for one call, per auth mode. ChatGPT mode coerces to the
        /// backend's accepted slugs (the settings-UI deadlock escape). API-key mode targets the
        /// Platform chat/completions API whose model ids are a DIFFERENT set — the stored or
        /// manual model is sent verbatim (the pre-port behavior), with a Platform-valid default
        /// when unset, so codex slugs are never sent to api.openai.com.
        /// </summary>
        private string ResolveWireModel(CredentialResult creds, string? requestModel)
        {
            if (IsChatGptMode(creds))
            {
                return NormalizeCodexModel(string.IsNullOrWhiteSpace(requestModel) ? _model : requestModel);
            }

            if (!string.IsNullOrWhiteSpace(requestModel)) return requestModel!;
            return string.IsNullOrWhiteSpace(_model) ? BrainarrConstants.DefaultOpenAICodexApiModel : _model;
        }

        /// <inheritdoc />
        public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var creds = LoadCreds(out var hint);
            if (creds == null)
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
                var probeRequest = new LlmRequest
                {
                    Prompt = "Reply with exactly: OK",
                    SystemPrompt = "You are a helpful assistant.",
                    MaxTokens = 16,
                };

                var wireModel = ResolveWireModel(creds, null);

                // One overall test budget bounds the WHOLE probe — initial send, a token refresh,
                // and the retry — so a slow/blackholed auth.openai.com cannot stretch "Test" to
                // send-timeout + refresh-timeout + send-timeout.
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));

                if (IsChatGptMode(creds))
                {
                    var http = await SendResponsesWithRefreshAsync(creds, probeRequest, useTestTimeout: false, budget.Token).ConfigureAwait(false);
                    sw.Stop();
                    if (http.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        return ProviderHealthResult.Unhealthy($"HTTP {(int)http.StatusCode}", sw.Elapsed, ProviderIdConst, "subscription", wireModel, errorCode: ((int)http.StatusCode).ToString());
                    }

                    // HTTP 200 does not by itself mean healthy: the stream may carry a
                    // response.failed/error event (the case CompleteAsync deliberately surfaces).
                    // Reporting Healthy for it would green-light a credential that cannot serve.
                    var parsed = CodexSseParser.Parse(http.Content);
                    if (!string.IsNullOrEmpty(parsed.ErrorDetail) && string.IsNullOrWhiteSpace(parsed.Text))
                    {
                        return ProviderHealthResult.Unhealthy($"Stream error: {Truncate(parsed.ErrorDetail)}", sw.Elapsed, ProviderIdConst, "subscription", wireModel, errorCode: "StreamFailed");
                    }

                    return ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "subscription", wireModel);
                }

                var response = await SendChatCompletionsAsync(creds.Token!, BuildChatCompletionsBody(probeRequest, wireModel), useTestTimeout: false, budget.Token).ConfigureAwait(false);
                sw.Stop();
                return response.StatusCode == System.Net.HttpStatusCode.OK
                    ? ProviderHealthResult.Healthy(sw.Elapsed, ProviderIdConst, "apiKey", wireModel)
                    : ProviderHealthResult.Unhealthy($"HTTP {(int)response.StatusCode}", sw.Elapsed, ProviderIdConst, "apiKey", wireModel, errorCode: ((int)response.StatusCode).ToString());
            }
            catch (LlmProviderException lpe)
            {
                return ProviderHealthResult.Unhealthy(lpe.Message, sw.Elapsed, ProviderIdConst, creds.AuthMode ?? "subscription", _model, errorCode: lpe.ErrorCode.ToString());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ProviderHealthResult.Unhealthy(ex.Message, sw.Elapsed, ProviderIdConst, creds.AuthMode ?? "subscription", _model);
            }
        }

        /// <inheritdoc />
        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            using var _scope = PluginLogContext.Push("Brainarr", "LlmComplete", provider: ProviderIdConst);

            // Auth circuit pre-flight keyed by credentials path. Subscription providers can't hash a
            // fresh API key (the bearer is loaded per-call from disk), so the path — which uniquely
            // identifies the user's Codex CLI session file — is the next-best stable identity.
            if (_authCircuit.IsOpen(ProviderIdConst, _credentialsPath, out var circuitReason))
            {
                throw new AuthenticationException(ProviderIdConst, LlmErrorCode.AuthenticationFailed,
                    "Auth circuit open: " + circuitReason);
            }

            var creds = LoadCreds(out var hint);
            if (creds == null)
            {
                throw new AuthenticationException(ProviderIdConst, LlmErrorCode.AuthenticationFailed,
                    hint ?? "OpenAI Codex credentials not available");
            }

            LlmResponse result;
            try
            {
                result = IsChatGptMode(creds)
                    ? await CompleteViaResponsesAsync(creds, request, cancellationToken).ConfigureAwait(false)
                    : await CompleteViaChatCompletionsAsync(creds, request, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (LlmProviderException lpe) when (
                lpe.ErrorCode == LlmErrorCode.AuthenticationFailed ||
                lpe.ErrorCode == LlmErrorCode.AuthorizationFailed)
            {
                _authCircuit.RecordAuthFailure(ProviderIdConst, _credentialsPath, lpe);
                throw;
            }

            _authCircuit.RecordSuccess(ProviderIdConst, _credentialsPath);
            return result;
        }

        /// <inheritdoc />
        public IAsyncEnumerable<LlmStreamChunk>? StreamAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            // The backend streams SSE, but we buffer + reconstruct rather than surface chunks.
            return null;
        }

        // ---------------------------------------------------------------------
        // ChatGPT-backend Responses path
        // ---------------------------------------------------------------------

        /// <summary>
        /// Sends a Responses request and, on an auth failure, refreshes the access token once and
        /// retries. Shared by <see cref="CompleteAsync"/> and <see cref="CheckHealthAsync"/> so the
        /// connection Test behaves like a real run: the access token is a ~10-day JWT, so without a
        /// refresh here a Test taken after expiry goes red while a concurrent sync succeeds — and
        /// Lidarr refuses to save an import list whose Test failed, stranding the user.
        /// </summary>
        private async Task<HttpCallResult> SendResponsesWithRefreshAsync(
            CredentialResult creds, LlmRequest request, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var http = await SendResponsesAsync(creds, request, useTestTimeout, cancellationToken).ConfigureAwait(false);

            // Only 401 is treated as a stale token. A 403 is a plan/quota denial at this
            // backend: rotating the (single-use) refresh token for it would burn a rotation
            // for nothing and interact badly with a failed persist.
            if (http.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var refreshed = await TryRefreshAsync(creds, cancellationToken).ConfigureAwait(false);
                if (refreshed != null)
                {
                    http = await SendResponsesAsync(refreshed, request, useTestTimeout, cancellationToken).ConfigureAwait(false);
                }
            }

            return http;
        }

        private async Task<LlmResponse> CompleteViaResponsesAsync(CredentialResult creds, LlmRequest request, CancellationToken cancellationToken)
        {
            var http = await SendResponsesWithRefreshAsync(creds, request, useTestTimeout: false, cancellationToken).ConfigureAwait(false);

            if (http.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var ex = LlmErrorMapper.MapHttpError(ProviderIdConst, (int)http.StatusCode, Truncate(http.Content), http.RetryAfter, inner: null);
                if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                {
                    _authCircuit.RecordAuthFailure(ProviderIdConst, _credentialsPath, ex);
                }
                throw ex;
            }

            var parsed = CodexSseParser.Parse(http.Content);

            // The transport succeeded (HTTP 200) but the stream itself can still report failure —
            // `response.failed` / `error` events carry a server-side error, content-filter stop, or
            // quota trip. Surfacing that as an exception matters: returning it as an empty-but-
            // successful completion would log "Generated 0 validated recommendations" with no cause,
            // AND would call RecordSuccess on the auth circuit for what may be a credential problem.
            // Only raise when there is no usable text, so a stream that errored after emitting a
            // complete answer still returns the answer.
            if (!string.IsNullOrEmpty(parsed.ErrorDetail) && string.IsNullOrWhiteSpace(parsed.Text))
            {
                throw LlmErrorMapper.MapException(ProviderIdConst,
                    new InvalidOperationException($"OpenAI Codex stream reported an error: {Truncate(parsed.ErrorDetail)}"));
            }

            return new LlmResponse
            {
                Content = parsed.Text,
                FinishReason = parsed.FinishReason,
                Usage = (parsed.InputTokens.HasValue || parsed.OutputTokens.HasValue)
                    ? new LlmUsage { InputTokens = parsed.InputTokens ?? 0, OutputTokens = parsed.OutputTokens ?? 0 }
                    : null,
            };
        }

        private async Task<HttpCallResult> SendResponsesAsync(CredentialResult creds, LlmRequest request, bool useTestTimeout, CancellationToken cancellationToken)
        {
            var body = BuildResponsesBody(request, ResolveWireModel(creds, request.Model));

            using var req = new HttpRequestMessage(HttpMethod.Post, BrainarrConstants.OpenAICodexResponsesUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {creds.Token}");
            if (!string.IsNullOrWhiteSpace(creds.AccountId))
            {
                req.Headers.TryAddWithoutValidation("chatgpt-account-id", creds.AccountId);
            }
            req.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
            req.Headers.TryAddWithoutValidation("originator", BrainarrConstants.OpenAICodexOriginator);
            req.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
            req.Headers.TryAddWithoutValidation("session_id", Guid.NewGuid().ToString());
            req.Headers.TryAddWithoutValidation("Accept", "text/event-stream");

            var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Content = content;

            var seconds = useTestTimeout
                ? BrainarrConstants.TestConnectionTimeout
                : TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(seconds));

            try
            {
                using var response = await _rawClient
                    .SendAsync(req, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                    .ConfigureAwait(false);

                var respBody = response.Content != null
                    ? await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false)
                    : string.Empty;

                return new HttpCallResult(response.StatusCode, respBody, LlmErrorMapper.ParseRetryAfterHeader(response.Headers.RetryAfter));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw LlmErrorMapper.MapException(ProviderIdConst,
                    new TimeoutException($"OpenAI Codex request timed out after {seconds}s. Raise 'AI Request Timeout' in the import list's advanced settings if the model is slow."));
            }
            catch (Exception ex) when (ex is not LlmProviderException)
            {
                throw LlmErrorMapper.MapException(ProviderIdConst, ex);
            }
        }

        private async Task<CredentialResult?> TryRefreshAsync(CredentialResult currentCreds, CancellationToken cancellationToken)
        {
            CodexRefreshResult refresh;
            try
            {
                refresh = _refreshOverride != null
                    ? await _refreshOverride(cancellationToken).ConfigureAwait(false)
                    : await CodexTokenRefresher.RefreshAsync(_credentialsPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            // A cancelled run must abort the run, not be downgraded to "refresh failed" — otherwise the
            // caller falls through to mapping the original 401 into an AuthenticationException, which
            // also records an auth failure against the credential for what was only a cancellation.
            // The `when` guard is load-bearing: the refresher's OWN 30s HTTP timeout also surfaces as
            // OperationCanceled while the run token is not cancelled, and that must stay a recoverable
            // refresh failure. (CLAUDE.md: cancellation must propagate through the whole chain.)
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn($"OpenAI Codex token refresh threw: {ex.Message}");
                return null;
            }

            if (!refresh.IsSuccess)
            {
                if (!string.IsNullOrEmpty(refresh.AccessToken))
                {
                    // Persistence failed AFTER the server consumed the old refresh token: the
                    // fresh access token is still valid, so complete this run with it (the on-disk
                    // file is stale, hence the loud error), rather than failing a request we hold
                    // working credentials for.
                    _logger.Error($"OpenAI Codex token rotation could not be persisted: {refresh.ErrorMessage}");
                    return currentCreds.WithToken(refresh.AccessToken);
                }
                _logger.Warn($"OpenAI Codex token refresh failed: {refresh.ErrorMessage}");
                return null;
            }

            _logger.Info("OpenAI Codex token refreshed; retrying request.");
            // Reload from disk so we pick up the rotated account_id/expiry alongside the new token.
            var reloaded = SubscriptionCredentialLoader.LoadCodexCredentials(_credentialsPath);
            return reloaded.IsSuccess ? reloaded : null;
        }

        // Responses API body confirmed working against the ChatGPT backend (2026-08). Minimal shape:
        // instructions (system), input (messages), stream:true (REQUIRED — the backend does not answer
        // without it), store:false.
        //
        // IMPORTANT — do NOT add `max_output_tokens` here. Unlike every other provider in this plugin,
        // this backend REJECTS it outright: 400 {"detail":"Unsupported parameter: max_output_tokens"}
        // (live-confirmed 2026-08). So request.MaxTokens — the pipeline's timeout-aware output budget —
        // cannot be honoured on this path and is deliberately dropped; sending it "for consistency with
        // the other providers" breaks every request. The consequence is that output length is bounded
        // only by the per-request timeout, so a slow/verbose run hits the linked-CTS deadline with no
        // body to salvage. The mitigation is the user-facing AI Request Timeout (raise to 60s+ for these
        // reasoning models), which the timeout error message points at.
        //
        // `temperature` and `reasoning` are also omitted: temperature is not part of this contract, and
        // reasoning effort measurably does not help (12.6s at effort=none vs 14.8s unset for a full
        // recommendation list — the cost is generation+network, not a reasoning preamble).
        private object BuildResponsesBody(LlmRequest request, string wireModel)
        {
            var model = wireModel;
            var instructions = string.IsNullOrWhiteSpace(request.SystemPrompt) ? "You are a helpful assistant." : request.SystemPrompt;

            return new
            {
                model,
                instructions,
                input = new[]
                {
                    new
                    {
                        type = "message",
                        role = "user",
                        content = new[] { new { type = "input_text", text = request.Prompt } },
                    },
                },
                stream = true,
                store = false,
            };
        }

        // ---------------------------------------------------------------------
        // API-key (Platform chat/completions) fallback path
        // ---------------------------------------------------------------------

        private async Task<LlmResponse> CompleteViaChatCompletionsAsync(CredentialResult creds, LlmRequest request, CancellationToken cancellationToken)
        {
            var response = await SendChatCompletionsAsync(creds.Token!, BuildChatCompletionsBody(request, ResolveWireModel(creds, request.Model)), useTestTimeout: false, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var ex = LlmErrorMapper.MapHttpError(ProviderIdConst, (int)response.StatusCode, Truncate(response.Content), BrainarrHttpResponseHelpers.ParseRetryAfter(response), inner: null);
                if (ex.ErrorCode == LlmErrorCode.AuthenticationFailed || ex.ErrorCode == LlmErrorCode.AuthorizationFailed)
                {
                    _authCircuit.RecordAuthFailure(ProviderIdConst, _credentialsPath, ex);
                }
                throw ex;
            }

            return ParseChatCompletion(response.Content ?? string.Empty);
        }

        private object BuildChatCompletionsBody(LlmRequest request, string wireModel)
        {
            var temp = (double?)request.Temperature ?? 0.8;
            var maxTokens = request.MaxTokens ?? 2000;
            var modelRaw = wireModel;

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

        private async Task<HttpResponse> SendChatCompletionsAsync(string token, object body, bool useTestTimeout, CancellationToken cancellationToken)
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
                return await HttpProviderClient.ExecuteWithCt(_httpClient, request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpException hex) when (hex.Response != null)
            {
                throw LlmErrorMapper.MapHttpError(ProviderIdConst, (int)hex.Response.StatusCode, Truncate(hex.Response.Content), BrainarrHttpResponseHelpers.ParseRetryAfter(hex.Response), hex);
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

        private static LlmResponse ParseChatCompletion(string content)
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
                        ? new LlmUsage { InputTokens = parsed.Usage.PromptTokens, OutputTokens = parsed.Usage.CompletionTokens }
                        : null,
                };
            }
            catch
            {
                return new LlmResponse { Content = content };
            }
        }

        // ---------------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------------

        private static bool IsChatGptMode(CredentialResult creds)
            => !string.Equals(creds.AuthMode, "apikey", StringComparison.OrdinalIgnoreCase);

        private CredentialResult? LoadCreds(out string? hint)
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
            return result;
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
                        "OpenAI Codex token rejected. Run 'codex auth login' on the host to re-authenticate; the plugin auto-refreshes the token while the refresh_token stays valid.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                LlmErrorCode.QuotaExceeded =>
                    new BrainarrLlmHint(
                        "OpenAI subscription quota exhausted. Check your ChatGPT plan usage.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.RateLimited =>
                    new BrainarrLlmHint(
                        "OpenAI Codex rate limit exceeded. Wait a moment and retry.",
                        BrainarrConstants.DocsOpenAIRateLimit),
                LlmErrorCode.ModelNotFound =>
                    new BrainarrLlmHint(
                        $"Model '{_model}' is not available for your ChatGPT plan via Codex. Try 'gpt-5.6-terra' or 'gpt-5.6-luna'.",
                        BrainarrConstants.DocsOpenAIInvalidKey),
                _ => null,
            };
        }

        // -- helpers / DTOs ---------------------------------------------------
        private readonly struct HttpCallResult
        {
            public HttpCallResult(System.Net.HttpStatusCode statusCode, string content, TimeSpan? retryAfter)
            {
                StatusCode = statusCode;
                Content = content;
                RetryAfter = retryAfter;
            }

            public System.Net.HttpStatusCode StatusCode { get; }
            public string Content { get; }
            public TimeSpan? RetryAfter { get; }
        }

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
