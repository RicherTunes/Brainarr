using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core
{
    /// <summary>
    /// Abstract base class for HTTP-based chat/completion providers.
    /// Provides common infrastructure for request execution, error handling, and response parsing.
    /// </summary>
    public abstract class HttpChatProviderBase : IAIProvider
    {
        protected readonly IHttpClient HttpClient;
        protected readonly Logger Logger;
        protected readonly string ApiKey;
        protected string Model;
        protected readonly bool PreferStructured;
        protected readonly IHttpResilience? HttpResilience;

        private string? _lastUserMessage;
        private string? _lastLearnMoreUrl;

        protected HttpChatProviderBase(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            IHttpResilience? httpResilience = null)
        {
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{ProviderName} API key is required", nameof(apiKey));

            ApiKey = apiKey;
            Model = model ?? DefaultModel;
            PreferStructured = preferStructured;
            HttpResilience = httpResilience;
        }

        #region Abstract Members - Must Override

        /// <summary>Provider display name (e.g., "OpenAI", "Anthropic").</summary>
        public abstract string ProviderName { get; }

        /// <summary>Default model ID when none specified.</summary>
        protected abstract string DefaultModel { get; }

        /// <summary>Base API URL for chat completions.</summary>
        protected abstract string ApiUrl { get; }

        /// <summary>Configure provider-specific headers on the request builder.</summary>
        protected abstract void ConfigureHeaders(HttpRequestBuilder builder);

        /// <summary>Create the request body for a chat completion.</summary>
        protected abstract object CreateRequestBody(string systemPrompt, string userPrompt, int maxTokens, double temperature);

        /// <summary>Extract the completion text content from the response body.</summary>
        protected abstract string? ExtractContent(string responseBody);

        #endregion

        #region Virtual Members - Can Override

        /// <summary>Default request timeout in seconds.</summary>
        protected virtual int DefaultTimeoutSeconds => BrainarrConstants.DefaultAITimeout;

        /// <summary>Default test connection timeout in seconds.</summary>
        protected virtual int TestConnectionTimeoutSeconds => BrainarrConstants.TestConnectionTimeout;

        /// <summary>Maximum retries for transient failures.</summary>
        protected virtual int MaxRetries => 3;

        /// <summary>Maximum concurrent requests per host.</summary>
        protected virtual int MaxConcurrencyPerHost => 2;

        /// <summary>Temperature for recommendation requests.</summary>
        protected virtual double DefaultTemperature => 0.7;

        /// <summary>Max tokens for recommendation requests.</summary>
        protected virtual int DefaultMaxTokens => 2000;

        /// <summary>
        /// Determine if the response indicates a retryable error.
        /// Override to add provider-specific retry logic.
        /// </summary>
        protected virtual bool ShouldRetry(HttpResponse response)
        {
            var code = (int)response.StatusCode;
            return code == 429 || code == 408 || (code >= 500 && code <= 504);
        }

        /// <summary>
        /// Map an HTTP response to a provider error.
        /// Override to add provider-specific error codes/messages.
        /// </summary>
        protected virtual ProviderError MapError(HttpResponse response)
        {
            return ProviderError.FromHttpCode((int)response.StatusCode, response.Content);
        }

        /// <summary>
        /// Capture user-facing hints for error conditions.
        /// Override to provide provider-specific documentation links.
        /// </summary>
        protected virtual void CaptureUserHint(HttpResponse response)
        {
            _lastUserMessage = null;
            _lastLearnMoreUrl = null;

            var code = (int)response.StatusCode;
            if (code == 401)
            {
                _lastUserMessage = $"Invalid {ProviderName} API key. Check your settings.";
            }
            else if (code == 429)
            {
                _lastUserMessage = $"{ProviderName} rate limit exceeded. Wait and retry.";
            }
        }

        /// <summary>
        /// Set the user hint and learn more URL.
        /// </summary>
        protected void SetUserHint(string? message, string? learnMoreUrl = null)
        {
            _lastUserMessage = message;
            _lastLearnMoreUrl = learnMoreUrl;
        }

        /// <summary>
        /// Parse the response content into recommendations.
        /// Override for provider-specific response handling.
        /// </summary>
        protected virtual List<Recommendation> ParseRecommendations(string content)
        {
            return RecommendationJsonParser.Parse(content, Logger);
        }

        /// <summary>
        /// Log token usage from the response.
        /// Override to extract provider-specific usage metrics.
        /// </summary>
        protected virtual void LogTokenUsage(string responseBody)
        {
            // Default: no-op. Providers can override to log usage.
        }

        #endregion

        #region IAIProvider Implementation

        public Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
                TimeoutContext.GetSecondsOrDefault(DefaultTimeoutSeconds)));
            return GetRecommendationsAsync(prompt, cts.Token);
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N").Substring(0, 8);
            var sw = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                Logger.LogRequestError(ProviderName, "GetRecommendations", correlationId, "EMPTY_PROMPT", "Empty prompt provided");
                return new List<Recommendation>();
            }

            var systemPrompt = GetSystemPrompt();
            var userPrompt = prompt;
            var temperature = TemperaturePolicy.FromPrompt(prompt, DefaultTemperature);

            Logger.LogRequestStart(ProviderName, "GetRecommendations", correlationId, Model);

            try
            {
                var response = await ExecuteCompletionAsync(
                    systemPrompt,
                    userPrompt,
                    DefaultMaxTokens,
                    temperature,
                    cancellationToken).ConfigureAwait(false);

                if (response == null || !response.HasHttpError && string.IsNullOrEmpty(response.Content))
                {
                    Logger.LogRequestError(ProviderName, "GetRecommendations", correlationId, "EMPTY_RESPONSE", "Empty response received");
                    return new List<Recommendation>();
                }

                if (response.HasHttpError)
                {
                    var error = MapError(response);
                    CaptureUserHint(response);

                    var code = (int)response.StatusCode;
                    if (code == 429)
                    {
                        Logger.LogRateLimited(ProviderName, correlationId);
                    }
                    else if (code == 401)
                    {
                        Logger.LogAuthFail(ProviderName, correlationId, $"HTTP {code}");
                    }
                    else
                    {
                        Logger.LogRequestError(ProviderName, "GetRecommendations", correlationId, $"HTTP_{code}", error.Category.ToString());
                    }

                    return new List<Recommendation>();
                }

                LogTokenUsage(response.Content);

                var content = ExtractContent(response.Content);
                if (string.IsNullOrEmpty(content))
                {
                    Logger.DebugWithCorrelation($"[{ProviderName}] No content in response, trying raw parse", correlationId);
                    return ParseRecommendations(response.Content ?? string.Empty);
                }

                LogDebugPayload("Response content", content);

                var recommendations = ParseRecommendations(content);
                if (recommendations.Count == 0)
                {
                    recommendations = ParseRecommendations(response.Content ?? string.Empty);
                }

                Logger.LogRequestComplete(ProviderName, "GetRecommendations", correlationId, sw.ElapsedMilliseconds);
                Logger.Info($"[{ProviderName}] Parsed {recommendations.Count} recommendations | CorrelationId={correlationId}");

                return recommendations;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Logger.DebugWithCorrelation($"[{ProviderName}] Request cancelled", correlationId);
                return new List<Recommendation>();
            }
            catch (TaskCanceledException)
            {
                Logger.LogRequestError(ProviderName, "GetRecommendations", correlationId, "TIMEOUT", "Request timed out");
                return new List<Recommendation>();
            }
            catch (Exception ex)
            {
                Logger.LogRequestError(ProviderName, "GetRecommendations", correlationId, "UNKNOWN", ex.Message, ex);
                return new List<Recommendation>();
            }
        }

        public Task<bool> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TestConnectionTimeoutSeconds));
            return TestConnectionAsync(cts.Token);
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                var response = await ExecuteCompletionAsync(
                    "You are a test assistant.",
                    "Say 'ok'",
                    maxTokens: 10,
                    temperature: 0,
                    cancellationToken).ConfigureAwait(false);

                if (response == null)
                {
                    Logger.LogHealthCheckFail(ProviderName, "No response received");
                    return false;
                }

                if (response.HasHttpError)
                {
                    CaptureUserHint(response);
                    Logger.LogHealthCheckFail(ProviderName, $"HTTP {response.StatusCode}");
                    return false;
                }

                Logger.LogHealthCheckPass(ProviderName, sw.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogHealthCheckFail(ProviderName, ex.Message);
                return false;
            }
        }

        public Task<List<string>> GetAvailableModelsAsync()
        {
            // Default: return empty. Providers with model discovery can override.
            return Task.FromResult(new List<string>());
        }

        public string? GetLastUserMessage() => _lastUserMessage;
        public string? GetLearnMoreUrl() => _lastLearnMoreUrl;

        public virtual void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                Model = modelName;
            }
        }

        #endregion

        #region Protected Helpers

        /// <summary>
        /// Execute a chat completion request with resilience handling.
        /// </summary>
        protected async Task<HttpResponse?> ExecuteCompletionAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature,
            CancellationToken cancellationToken)
        {
            var body = CreateRequestBody(systemPrompt, userPrompt, maxTokens, temperature);
            var json = SecureJsonSerializer.Serialize(body);

            LogDebugPayload("Request body", json);

            var builder = new HttpRequestBuilder(ApiUrl);
            ConfigureHeaders(builder);
            builder.SetHeader("Content-Type", "application/json");

            var request = builder.Build();
            request.Method = HttpMethod.Post;
            request.SetContent(json);

            var timeoutSeconds = TimeoutContext.GetSecondsOrDefault(DefaultTimeoutSeconds);
            request.RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            return await ExecuteWithResilienceAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute an HTTP request with retry/resilience policy.
        /// </summary>
        protected async Task<HttpResponse?> ExecuteWithResilienceAsync(
            HttpRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (HttpResilience != null)
                {
                    return await HttpResilience.SendAsync(
                        templateRequest: request,
                        send: (req, token) => HttpClient.ExecuteAsync(req),
                        origin: $"provider:{ProviderName}:{Model}",
                        logger: Logger,
                        cancellationToken: cancellationToken,
                        maxRetries: MaxRetries,
                        maxConcurrencyPerHost: MaxConcurrencyPerHost,
                        retryBudget: null,
                        perRequestTimeout: request.RequestTimeout).ConfigureAwait(false);
                }

                // Fallback to static resilience policy
                return await ResiliencePolicy.WithHttpResilienceAsync(
                    request,
                    (req, token) => HttpClient.ExecuteAsync(req),
                    origin: $"provider:{ProviderName}:{Model}",
                    logger: Logger,
                    cancellationToken: cancellationToken,
                    maxRetries: MaxRetries,
                    shouldRetry: ShouldRetry).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{ProviderName}] HTTP execution error");
                return null;
            }
        }

        /// <summary>
        /// Get the system prompt for recommendations.
        /// </summary>
        protected virtual string GetSystemPrompt()
        {
            return "You are a music recommendation expert. Respond with a JSON array of recommendations. " +
                   "Each recommendation should have: artist, album, genre, reason, confidence (0.0-1.0), year.";
        }

        /// <summary>
        /// Log debug payload when debug flags are enabled.
        /// </summary>
        protected void LogDebugPayload(string label, string? content)
        {
            if (!DebugFlags.ProviderPayload || string.IsNullOrEmpty(content))
                return;

            try
            {
                var snippet = content.Length > 4000
                    ? content.Substring(0, 4000) + "... [truncated]"
                    : content;
                Logger.InfoWithCorrelation($"[Brainarr Debug] {ProviderName} {label}: {snippet}");
            }
            catch
            {
                // Non-critical
            }
        }

        /// <summary>
        /// Get the cache key for format preference.
        /// </summary>
        protected string GetFormatCacheKey() => $"{ProviderName}:{Model}";

        /// <summary>
        /// Get cached format preference for this provider/model.
        /// </summary>
        protected bool GetCachedFormatPreference()
        {
            return FormatPreferenceCache.GetPreferStructuredOrDefault(GetFormatCacheKey(), PreferStructured);
        }

        /// <summary>
        /// Cache the format preference for this provider/model.
        /// </summary>
        protected void SetCachedFormatPreference(bool preferStructured)
        {
            FormatPreferenceCache.SetPreferStructured(GetFormatCacheKey(), preferStructured);
        }

        #endregion
    }
}
