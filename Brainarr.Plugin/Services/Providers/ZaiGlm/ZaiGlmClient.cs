using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ZaiGlm
{
    /// <summary>
    /// HTTP client layer for Z.AI GLM API.
    /// Handles request construction, authentication, and response handling with resilience.
    /// </summary>
    public class ZaiGlmClient
    {
        private readonly IHttpClient _httpClient;
        private readonly IHttpResilience? _httpResilience;
        private readonly Logger _logger;
        private readonly string _apiKey;

        private const string ApiUrl = BrainarrConstants.ZaiGlmChatCompletionsUrl;
        private const int MaxRetries = 3;
        private const int MaxConcurrencyPerHost = 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="ZaiGlmClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client for making API requests.</param>
        /// <param name="logger">The logger for diagnostic output.</param>
        /// <param name="apiKey">The Z.AI API key for authentication.</param>
        /// <param name="httpResilience">Optional HTTP resilience handler.</param>
        public ZaiGlmClient(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            IHttpResilience? httpResilience = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpResilience = httpResilience;

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI API key is required", nameof(apiKey));

            _apiKey = apiKey;
        }

        /// <summary>
        /// Sends a chat completion request to Z.AI GLM API.
        /// </summary>
        /// <param name="body">The request body object to serialize as JSON.</param>
        /// <param name="modelId">The model ID being used (for logging/origin tracking).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The HTTP response from the API.</returns>
        public async Task<HttpResponse> SendChatCompletionAsync(
            object body,
            string modelId,
            CancellationToken cancellationToken)
        {
            var request = BuildRequest(body);
            var origin = $"zai-glm:{modelId}";
            var timeoutSeconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            var perRequestTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            LogDebugPayload(body);

            if (_httpResilience != null)
            {
                return await _httpResilience.SendAsync(
                    templateRequest: request,
                    send: (req, token) => _httpClient.ExecuteAsync(req),
                    origin: origin,
                    logger: _logger,
                    cancellationToken: cancellationToken,
                    maxRetries: MaxRetries,
                    maxConcurrencyPerHost: MaxConcurrencyPerHost,
                    retryBudget: null,
                    perRequestTimeout: perRequestTimeout);
            }

            return await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithHttpResilienceAsync(
                request,
                (req, token) => _httpClient.ExecuteAsync(req),
                origin: origin,
                logger: _logger,
                cancellationToken: cancellationToken,
                maxRetries: MaxRetries,
                shouldRetry: resp => ZaiGlmErrorMapper.ShouldRetry((int)resp.StatusCode, resp.Content));
        }

        /// <summary>
        /// Sends a test connection request to verify API key validity.
        /// </summary>
        /// <param name="modelId">The model ID to use for the test.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The HTTP response from the API.</returns>
        public async Task<HttpResponse> SendTestConnectionAsync(
            string modelId,
            CancellationToken cancellationToken)
        {
            var body = new
            {
                model = modelId,
                messages = new[] { new { role = "user", content = "Reply with OK" } },
                max_tokens = 5
            };

            var request = BuildRequest(body);
            request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

            var origin = "zai-glm:test";

            return await NzbDrone.Core.ImportLists.Brainarr.Resilience.ResiliencePolicy.WithResilienceAsync(
                _ => _httpClient.ExecuteAsync(request),
                origin: origin,
                logger: _logger,
                cancellationToken: cancellationToken,
                timeoutSeconds: TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout),
                maxRetries: 2);
        }

        /// <summary>
        /// Checks if the response indicates success (HTTP 200 and no error in body).
        /// </summary>
        /// <param name="response">The HTTP response to check.</param>
        /// <returns>True if the response is successful.</returns>
        public static bool IsSuccessResponse(HttpResponse response)
        {
            if (response == null)
                return false;
            if (response.StatusCode != HttpStatusCode.OK)
                return false;
            if (ZaiGlmErrorMapper.HasErrorInBody(response.Content))
                return false;
            return true;
        }

        private HttpRequest BuildRequest(object body)
        {
            var request = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Authorization", $"Bearer {_apiKey}")
                .SetHeader("Content-Type", "application/json")
                .Build();

            request.Method = HttpMethod.Post;
            var json = SecureJsonSerializer.Serialize(body);
            request.SetContent(json);

            var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
            request.RequestTimeout = TimeSpan.FromSeconds(seconds);

            return request;
        }

        private void LogDebugPayload(object body)
        {
            if (!DebugFlags.ProviderPayload)
                return;

            try
            {
                var json = SecureJsonSerializer.Serialize(body);
                var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                _logger.Info($"[Brainarr Debug] Z.AI GLM endpoint: {ApiUrl}");
                _logger.Info($"[Brainarr Debug] Z.AI GLM request JSON: {snippet}");
            }
            catch
            {
                // Non-critical
            }
        }
    }
}
