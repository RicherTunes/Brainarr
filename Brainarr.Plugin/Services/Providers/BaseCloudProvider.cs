using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
// Avoid referencing Services.Core from Providers to satisfy layering guards
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Rate limit information extracted from provider HTTP response headers.
    /// </summary>
    public sealed record RateLimitHeaderInfo(int Remaining, DateTime ResetAt);

    /// <summary>
    /// Abstract base class for cloud AI providers.
    /// Provides common functionality for HTTP communication, error handling, and response parsing.
    /// </summary>
    public abstract class BaseCloudProvider : IAIProvider
    {
        /// <summary>
        /// Rate limit info extracted from the most recent HTTP response.
        /// Null when headers were absent or unparseable.
        /// </summary>
        public RateLimitHeaderInfo? LastRateLimitInfo { get; private set; }

        protected readonly IHttpClient _httpClient;
        protected readonly Logger _logger;
        protected readonly string _apiKey;
        protected string _model;
        protected readonly IRecommendationValidator _validator;

        /// <summary>
        /// Gets the API endpoint URL for this provider.
        /// </summary>
        protected abstract string ApiUrl { get; }

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public abstract string ProviderName { get; }

        /// <summary>
        /// Initializes a new instance of the BaseCloudProvider class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="apiKey">Provider API key (required)</param>
        /// <param name="model">Model to use</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is null or empty</exception>
        protected BaseCloudProvider(IHttpClient httpClient, Logger logger, string apiKey, string model, IRecommendationValidator? validator = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException($"{ProviderName} API key is required", nameof(apiKey));

            _apiKey = apiKey;
            _model = model ?? GetDefaultModel();
            _validator = validator ?? new RecommendationValidator(logger);

            _logger.Info($"Initialized {ProviderName} provider with model: {_model}");
        }

        /// <summary>
        /// Gets the default model for this provider when none is specified.
        /// </summary>
        protected abstract string GetDefaultModel();

        /// <summary>
        /// Creates the HTTP request headers specific to this provider.
        /// </summary>
        protected abstract void ConfigureHeaders(HttpRequestBuilder builder);

        /// <summary>
        /// Creates the request body for the AI prompt.
        /// </summary>
        protected abstract object CreateRequestBody(string prompt, int maxTokens = 2000);

        /// <summary>
        /// Parses the AI response and extracts recommendations.
        /// </summary>
        protected abstract List<Recommendation> ParseResponse(string responseContent);

        /// <summary>
        /// Gets music recommendations from the AI provider.
        /// </summary>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        /// <summary>
        /// Tests the connection to the AI provider.
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.TestConnectionTimeout)));
            return await TestConnectionAsync(cts.Token);
        }

        /// <summary>
        /// Updates the model used by this provider.
        /// </summary>
        public virtual void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"{ProviderName} model updated to: {modelName}");
            }
        }

        /// <summary>
        /// Executes an HTTP request to the AI provider.
        /// </summary>
        private async Task<HttpResponse> ExecuteRequestAsync(object requestBody, System.Threading.CancellationToken cancellationToken)
        {
            var builder = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Content-Type", "application/json");

            ConfigureHeaders(builder);

            var request = builder.Build();
            request.Method = HttpMethod.Post;
            var json = SecureJsonSerializer.Serialize(requestBody);
            request.SetContent(json);
            var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(seconds);

            // Optional sanitized payload logging for troubleshooting
            if (DebugFlags.ProviderPayload)
            {
                try
                {
                    var url = ApiUrl;
                    var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                    _logger.InfoWithCorrelation($"[Brainarr Debug] {ProviderName} endpoint: {url}");
                    _logger.InfoWithCorrelation($"[Brainarr Debug] {ProviderName} request JSON: {snippet}");
                }
                catch { /* never break the request on logging */ }
            }

            // Use the HTTP-specific resilience helper so we get per-host concurrency gates,
            // Retry-After handling, and adaptive limiter feedback.
            return await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                request,
                (req, ct) => _httpClient.ExecuteAsync(req), // IHttpClient has no CT overload; timeout is enforced above
                origin: $"{ProviderName}:{_model}",
                logger: _logger,
                cancellationToken: cancellationToken,
                maxRetries: 2,
                shouldRetry: null,
                limiter: null,
                retryBudget: TimeSpan.FromSeconds(12),
                maxConcurrencyPerHost: 8,
                perRequestTimeout: TimeSpan.FromSeconds(seconds));
        }

        // Cancellation-aware default implementation (cooperates with resilience delays)
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.Info($"Getting recommendations from {ProviderName}");
                var requestBody = CreateRequestBody(prompt);
                var response = await ExecuteRequestAsync(requestBody, cancellationToken);
                LastRateLimitInfo = ExtractRateLimitHeaders(response);
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"{ProviderName} API error: {response.StatusCode}");
                    var content = response.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(content))
                    {
                        var snippet = content.Substring(0, Math.Min(content.Length, 500));
                        _logger.Debug($"{ProviderName} API error body (truncated): {snippet}");
                    }
                    return new List<Recommendation>();
                }
                var recommendations = ParseResponse(response.Content);
                _logger.Info($"Received {recommendations.Count} recommendations from {ProviderName}");
                return recommendations;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Cancellation-aware default implementation
        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _logger.Info($"Testing connection to {ProviderName}");
                var testBody = CreateRequestBody("Reply with OK", 5);
                var response = await ExecuteRequestAsync(testBody, cancellationToken);
                LastRateLimitInfo = ExtractRateLimitHeaders(response);
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"{ProviderName} connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                return success;
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }


        /// <summary>
        /// Extracts rate limit information from common HTTP response headers.
        /// Supports OpenAI, Anthropic, Groq, and generic x-ratelimit patterns.
        /// </summary>
        internal static RateLimitHeaderInfo? ExtractRateLimitHeaders(HttpResponse response)
        {
            if (response?.Headers == null) return null;

            int? remaining = null;
            DateTime? resetAt = null;

            foreach (var header in response.Headers)
            {
                var key = header.Key;
                var value = (header.Value ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(value)) continue;

                // Match remaining-requests headers (OpenAI, Groq, generic)
                if (remaining == null &&
                    (key.Equals("x-ratelimit-remaining-requests", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("x-ratelimit-remaining", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("ratelimit-remaining", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("anthropic-ratelimit-requests-remaining", StringComparison.OrdinalIgnoreCase)))
                {
                    if (int.TryParse(value, out var r)) remaining = r;
                }

                // Match reset headers
                if (resetAt == null &&
                    (key.Equals("x-ratelimit-reset-requests", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("x-ratelimit-reset", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("ratelimit-reset", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("anthropic-ratelimit-requests-reset", StringComparison.OrdinalIgnoreCase)))
                {
                    // Try ISO-8601 date first, then seconds-from-now
                    if (DateTimeOffset.TryParse(value, out var dto))
                    {
                        resetAt = dto.UtcDateTime;
                    }
                    else if (int.TryParse(value, out var seconds))
                    {
                        resetAt = DateTime.UtcNow.AddSeconds(seconds);
                    }
                }
            }

            if (remaining.HasValue)
            {
                return new RateLimitHeaderInfo(remaining.Value, resetAt ?? DateTime.UtcNow.AddMinutes(1));
            }

            return null;
        }

        /// <summary>
        /// Helper method to parse individual recommendation from dynamic object.
        /// </summary>
        protected void ParseSingleRecommendation(dynamic item, List<Recommendation> recommendations)
        {
            try
            {
                var rec = new Recommendation
                {
                    Artist = item.artist?.ToString() ?? item.Artist?.ToString(),
                    Album = item.album?.ToString() ?? item.Album?.ToString(),
                    Genre = item.genre?.ToString() ?? item.Genre?.ToString() ?? "Unknown",
                    Confidence = item.confidence != null ? (double)item.confidence : 0.85,
                    Reason = item.reason?.ToString() ?? item.Reason?.ToString() ?? "Recommended based on your preferences"
                };

                // Allow artist-only recommendations (for artist mode) or full recommendations (for album mode)
                var isArtistOnly = string.IsNullOrWhiteSpace(rec.Album) && !string.IsNullOrWhiteSpace(rec.Artist);

                if (!string.IsNullOrWhiteSpace(rec.Artist))
                {
                    if (_validator.ValidateRecommendation(rec, isArtistOnly))
                    {
                        recommendations.Add(rec);
                        _logger.Debug($"Parsed recommendation: {rec.Artist} - {rec.Album ?? "[Artist Only]"}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to parse individual recommendation");
            }
        }
    }
}
