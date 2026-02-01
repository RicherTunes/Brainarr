using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Base class for HTTP-based AI providers in Brainarr.
    /// Provides common functionality for HTTP request execution, error handling,
    /// retry logic, and API key redaction.
    /// </summary>
    /// <remarks>
    /// This base class is specific to Brainarr's architecture (using NzbDrone's IHttpClient and NLog),
    /// unlike Common library's HttpChatProviderBase which uses standard .NET HttpClient.
    /// </remarks>
    public abstract class BrainarrHttpProviderBase : IAIProvider
    {
        /// <summary>
        /// HTTP client for API communication.
        /// </summary>
        protected readonly IHttpClient _httpClient;

        /// <summary>
        /// Logger for diagnostic information.
        /// </summary>
        protected readonly Logger _logger;

        /// <summary>
        /// API key for provider authentication.
        /// </summary>
        protected readonly string _apiKey;

        /// <summary>
        /// Model identifier for this provider.
        /// </summary>
        protected string _model;

        /// <summary>
        /// Last user-facing error message.
        /// </summary>
        private string? _lastUserMessage;

        /// <summary>
        /// Last learn-more URL for user guidance.
        /// </summary>
        private string? _lastLearnMoreUrl;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrainarrHttpProviderBase"/> class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication.</param>
        /// <param name="logger">Logger for diagnostics.</param>
        /// <param name="apiKey">API key for provider authentication.</param>
        /// <param name="model">Default model identifier.</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null.</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is null or empty.</exception>
        protected BrainarrHttpProviderBase(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string model = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key is required", nameof(apiKey));
            }

            _apiKey = apiKey;
            _model = model ?? GetDefaultModel();
        }

        /// <inheritdoc />
        public abstract string ProviderName { get; }

        /// <summary>
        /// Gets the API endpoint URL for this provider.
        /// </summary>
        protected abstract string ApiEndpoint { get; }

        /// <summary>
        /// Gets the default model identifier for this provider.
        /// </summary>
        /// <returns>Default model ID.</returns>
        protected abstract string GetDefaultModel();

        /// <summary>
        /// Builds the HTTP request headers for the provider.
        /// </summary>
        /// <param name="request">The HTTP request to configure headers on.</param>
        protected abstract void BuildRequestHeaders(HttpRequest request);

        /// <summary>
        /// Builds the HTTP request body for the given prompt.
        /// </summary>
        /// <param name="prompt">The user prompt.</param>
        /// <returns>Request body object to serialize as JSON.</returns>
        protected abstract object BuildRequestBody(string prompt);

        /// <summary>
        /// Parses the HTTP response content into recommendations.
        /// </summary>
        /// <param name="responseContent">The raw response content string.</param>
        /// <returns>List of parsed recommendations.</returns>
        protected abstract List<Recommendation> ParseResponse(string responseContent);

        /// <inheritdoc />
        public abstract Task<bool> TestConnectionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Tests connection with default timeout.
        /// </summary>
        public virtual async Task<bool> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout));
            return await TestConnectionAsync(cts.Token);
        }

        /// <inheritdoc />
        public virtual async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        /// <inheritdoc />
        public virtual async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    throw new ArgumentException("Prompt cannot be empty", nameof(prompt));
                }

                // Build and execute request
                var response = await ExecuteRequestAsync(prompt, cancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    HandleErrorResponse(response);
                    return new List<Recommendation>();
                }

                // Parse response
                var content = response.Content ?? string.Empty;
                return ParseResponse(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from {ProviderName}", ProviderName);
                return new List<Recommendation>();
            }
        }

        /// <inheritdoc />
        public virtual void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info("{ProviderName} model updated to: {ModelName}", ProviderName, modelName);
            }
        }

        /// <inheritdoc />
        public virtual string? GetLastUserMessage() => _lastUserMessage;

        /// <inheritdoc />
        public virtual string? GetLearnMoreUrl() => _lastLearnMoreUrl;

        /// <summary>
        /// Executes an HTTP request with basic error handling.
        /// </summary>
        /// <param name="prompt">The prompt to build the request from.</param>
        /// <param name="cancellationToken">Token to cancel the request.</param>
        /// <returns>The HTTP response.</returns>
        protected virtual async Task<HttpResponse> ExecuteRequestAsync(string prompt, CancellationToken cancellationToken)
        {
            var requestBody = BuildRequestBody(prompt);
            var request = BuildHttpRequest(requestBody);

            // Apply timeout
            var timeoutSeconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Execute with resilience
            return await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                _ => _httpClient.ExecuteAsync(request),
                origin: ProviderName.ToLowerInvariant(),
                logger: _logger,
                cancellationToken: cancellationToken,
                timeoutSeconds: timeoutSeconds,
                maxRetries: 3);
        }

        /// <summary>
        /// Builds an HTTP request from the request body.
        /// </summary>
        /// <param name="requestBody">The request body object.</param>
        /// <returns>The configured HTTP request.</returns>
        protected virtual HttpRequest BuildHttpRequest(object requestBody)
        {
            var request = new HttpRequestBuilder(ApiEndpoint)
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.Method = HttpMethod.Post;

            // Add provider-specific headers
            BuildRequestHeaders(request);

            // Serialize and set content
            var json = SecureJsonSerializer.Serialize(requestBody);
            request.SetContent(json);

            return request;
        }

        /// <summary>
        /// Handles error responses from the provider.
        /// </summary>
        /// <param name="response">The error response.</param>
        protected virtual void HandleErrorResponse(HttpResponse response)
        {
            var statusCode = (int)response.StatusCode;
            var content = response.Content ?? string.Empty;

            _logger.Error("{ProviderName} API error: {StatusCode}", ProviderName, statusCode);
            _logger.Debug("{ProviderName} API response details: {Content}",
                ProviderName,
                content.Substring(0, Math.Min(content.Length, 500)));

            // Map status codes to user-friendly messages
            MapErrorToUserMessage(statusCode, content);
        }

        /// <summary>
        /// Maps HTTP error status codes to user-facing error messages.
        /// </summary>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="responseContent">The response content.</param>
        protected virtual void MapErrorToUserMessage(int statusCode, string responseContent)
        {
            _lastUserMessage = null;
            _lastLearnMoreUrl = null;

            switch (statusCode)
            {
                case 401:
                    _lastUserMessage = $"Authentication failed for {ProviderName}. Please check your API key.";
                    break;
                case 403:
                    _lastUserMessage = $"Authorization failed for {ProviderName}. Please check your API permissions.";
                    break;
                case 429:
                    _lastUserMessage = $"Rate limit exceeded for {ProviderName}. Please wait a few minutes and try again.";
                    break;
                default:
                    if (statusCode >= 500 && statusCode < 600)
                    {
                        _lastUserMessage = $"{ProviderName} is experiencing server issues. Please try again later.";
                    }
                    break;
            }
        }

        /// <summary>
        /// Redacts the API key from content to prevent leakage in logs.
        /// </summary>
        /// <param name="content">The content to redact.</param>
        /// <returns>Content with API key redacted.</returns>
        protected string RedactApiKey(string content)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(_apiKey))
            {
                return content;
            }

            return content.Replace(_apiKey, "[REDACTED]");
        }

        /// <summary>
        /// Returns a string representation that redacts the API key.
        /// </summary>
        /// <returns>Safe string representation.</returns>
        public override string ToString()
        {
            return $"{ProviderName} (API Key: [REDACTED])";
        }
    }
}
