using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Errors;
using Lidarr.Plugin.Common.Observability;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using MelILogger = Microsoft.Extensions.Logging.ILogger;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Adapts a generic <see cref="ILlmProvider"/> from <c>Lidarr.Plugin.Common</c> to brainarr's
    /// music-domain <see cref="IAIProvider"/> seam.
    ///
    /// <para>
    /// Responsibilities:
    /// 1. Build an <see cref="LlmRequest"/> from a brainarr music-recommendation prompt.
    /// 2. Invoke <see cref="ILlmProvider.CompleteAsync"/> with timeout/cancellation propagation.
    /// 3. Parse the resulting <see cref="LlmResponse.Content"/> through
    ///    <see cref="RecommendationJsonParser"/> (kept music-domain-local — see audit finding #13).
    /// 4. Map <see cref="LlmProviderException"/> → empty result + best-effort user-facing hints,
    ///    so existing callers (which never expect an exception from <c>GetRecommendationsAsync</c>)
    ///    continue to work unchanged.
    /// </para>
    ///
    /// <para>
    /// All 60+ callers that depend on <see cref="IAIProvider"/> remain untouched: they receive
    /// <see cref="List{Recommendation}"/> as before. The adapter is the sole bridge between the
    /// generic LLM contract and brainarr's recommendation domain.
    /// </para>
    /// </summary>
    public class LlmProviderAdapter : IAIProvider
    {
        private readonly ILlmProvider _llm;
        private readonly Logger _logger;
        private readonly MelILogger _msLogger;
        private readonly string _systemPrompt;
        private readonly float _temperature;
        private readonly int _maxTokens;
        private string? _lastUserMessage;
        private string? _lastUserLearnMoreUrl;

        public LlmProviderAdapter(
            ILlmProvider llm,
            Logger logger,
            string? systemPrompt = null,
            float temperature = 0.8f,
            int maxTokens = 2000)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _msLogger = NLogShim.For(logger);
            _systemPrompt = systemPrompt
                ?? "You are a knowledgeable music recommendation assistant. Always respond with valid JSON containing music recommendations.";
            _temperature = temperature;
            _maxTokens = maxTokens;
        }

        /// <inheritdoc />
        public string ProviderName => _llm.DisplayName;

        /// <summary>
        /// Exposes the wrapped <see cref="ILlmProvider"/> for tests and advanced callers
        /// that want to bypass the recommendation-parsing layer.
        /// </summary>
        public ILlmProvider Inner => _llm;

        /// <inheritdoc />
        public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return GetRecommendationsAsync(prompt, cts.Token);
        }

        /// <inheritdoc />
        public async Task<List<Recommendation>> GetRecommendationsAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.Warn($"{_llm.DisplayName}: empty prompt — returning no recommendations");
                return new List<Recommendation>();
            }

            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var sw = Stopwatch.StartNew();

            _msLogger.LogRequestStart(
                plugin: "Brainarr",
                provider: _llm.ProviderId,
                operation: "completion",
                correlationId: correlationId);

            try
            {
                var request = new LlmRequest
                {
                    Prompt = prompt,
                    SystemPrompt = _llm.Capabilities.Flags.HasFlag(LlmCapabilityFlags.SystemPrompt) ? _systemPrompt : null,
                    Temperature = _temperature,
                    MaxTokens = _maxTokens,
                    Timeout = TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)),
                };

                var response = await _llm.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                sw.Stop();

                _msLogger.LogRequestComplete(
                    plugin: "Brainarr",
                    provider: _llm.ProviderId,
                    operation: "completion",
                    correlationId: correlationId,
                    elapsedMs: sw.ElapsedMilliseconds,
                    inputTokens: response.Usage?.InputTokens,
                    outputTokens: response.Usage?.OutputTokens);

                var content = response.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.Warn($"Empty response from {_llm.DisplayName}");
                    return new List<Recommendation>();
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (LlmProviderException lpe)
            {
                sw.Stop();
                _msLogger.LogRequestError(
                    plugin: "Brainarr",
                    provider: _llm.ProviderId,
                    operation: "completion",
                    correlationId: correlationId,
                    errorCode: lpe.ErrorCode.ToString(),
                    errorMessage: lpe.Message,
                    exception: lpe);

                CaptureUserHint(lpe);
                return new List<Recommendation>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _msLogger.LogRequestError(
                    plugin: "Brainarr",
                    provider: _llm.ProviderId,
                    operation: "completion",
                    correlationId: correlationId,
                    errorCode: "Unexpected",
                    errorMessage: ex.Message,
                    exception: ex);

                _logger.Error(ex, $"Error getting recommendations from {_llm.DisplayName}");
                return new List<Recommendation>();
            }
        }

        /// <inheritdoc />
        public Task<bool> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
            return TestConnectionAsync(cts.Token);
        }

        /// <inheritdoc />
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastUserMessage = null;
            _lastUserLearnMoreUrl = null;

            var sw = Stopwatch.StartNew();
            try
            {
                var health = await _llm.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (health.IsHealthy)
                {
                    _msLogger.LogHealthCheckPass("Brainarr", _llm.ProviderId, sw.ElapsedMilliseconds);
                    return true;
                }

                _msLogger.LogHealthCheckFail("Brainarr", _llm.ProviderId, health.StatusMessage ?? "unknown");
                return false;
            }
            catch (LlmProviderException lpe)
            {
                CaptureUserHint(lpe);
                _msLogger.LogHealthCheckFail("Brainarr", _llm.ProviderId, lpe.Message);
                return false;
            }
            catch (Exception ex)
            {
                _msLogger.LogHealthCheckFail("Brainarr", _llm.ProviderId, ex.Message);
                return false;
            }
        }

        /// <inheritdoc />
        public void UpdateModel(string modelName)
        {
            // The wrapped ILlmProvider is constructed with its model up-front; brainarr's
            // existing factory recreates providers on each call, so model updates flow through
            // the normal construction path. This method exists for source-compat with the
            // legacy IAIProvider contract.
            if (_llm is IBrainarrLlmModelMutable mutable && !string.IsNullOrWhiteSpace(modelName))
            {
                mutable.UpdateModel(modelName);
            }
        }

        /// <inheritdoc />
        public string? GetLastUserMessage() => _lastUserMessage;

        /// <inheritdoc />
        public string? GetLearnMoreUrl() => _lastUserLearnMoreUrl;

        /// <summary>
        /// Allows providers to surface user-facing hints from
        /// <see cref="LlmProviderException"/> without coupling the adapter to provider-specific
        /// docs URLs. Providers may implement <see cref="IBrainarrLlmHintSource"/> to enrich
        /// the hint text and learn-more link based on the exception they raised.
        /// </summary>
        private void CaptureUserHint(LlmProviderException exception)
        {
            if (_llm is IBrainarrLlmHintSource hintSource)
            {
                var hint = hintSource.GetUserHint(exception);
                if (hint != null)
                {
                    _lastUserMessage = hint.Message;
                    _lastUserLearnMoreUrl = hint.LearnMoreUrl;
                    return;
                }
            }

            // Generic fallback derived from error code only — providers should override via
            // IBrainarrLlmHintSource for richer guidance.
            _lastUserMessage = exception.ErrorCode switch
            {
                LlmErrorCode.AuthenticationFailed => $"{_llm.DisplayName}: invalid API key.",
                LlmErrorCode.AuthorizationFailed => $"{_llm.DisplayName}: access denied.",
                LlmErrorCode.RateLimited => $"{_llm.DisplayName}: rate limit exceeded.",
                LlmErrorCode.QuotaExceeded => $"{_llm.DisplayName}: quota or credits exhausted.",
                LlmErrorCode.ModelNotFound => $"{_llm.DisplayName}: model not found or unsupported.",
                LlmErrorCode.ProviderUnavailable => $"{_llm.DisplayName}: provider temporarily unavailable.",
                LlmErrorCode.ProviderOverloaded => $"{_llm.DisplayName}: provider overloaded — retry later.",
                LlmErrorCode.Timeout => $"{_llm.DisplayName}: request timed out.",
                _ => null,
            };
        }
    }

    /// <summary>
    /// Optional contract for <see cref="ILlmProvider"/> implementations that want to surface
    /// brainarr-specific user-facing hints when an <see cref="LlmProviderException"/> escapes.
    /// Pure-common providers (e.g. <c>Lidarr.Plugin.Common.Providers.ClaudeCode.ClaudeCodeProvider</c>)
    /// don't need to implement this — the adapter falls back to the generic mapping above.
    /// </summary>
    public interface IBrainarrLlmHintSource
    {
        BrainarrLlmHint? GetUserHint(LlmProviderException exception);
    }

    /// <summary>
    /// Optional contract for providers that allow late-binding model selection. Most brainarr
    /// providers are recreated on each request via the registry, so this is rarely needed.
    /// </summary>
    public interface IBrainarrLlmModelMutable
    {
        void UpdateModel(string modelName);
    }

    public sealed record BrainarrLlmHint(string Message, string? LearnMoreUrl);
}
