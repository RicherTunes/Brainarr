using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ZaiGlm;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// AI provider implementation for Z.AI GLM models.
    /// Supports GLM-4.7, GLM-4.6, GLM-4.5 series with OpenAI-compatible API.
    /// </summary>
    public class ZaiGlmProvider : IAIProvider
    {
        private readonly ZaiGlmClient _client;
        private readonly Logger _logger;
        private string _model;
        private readonly bool _preferStructured;

        /// <summary>
        /// Gets the display name of the provider.
        /// </summary>
        public string ProviderName => "Z.AI GLM";

        /// <summary>
        /// Initializes a new instance of the <see cref="ZaiGlmProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client for making API requests.</param>
        /// <param name="logger">The logger for diagnostic output.</param>
        /// <param name="apiKey">The Z.AI API key for authentication.</param>
        /// <param name="model">The model to use (defaults to glm-4.7-flash).</param>
        /// <param name="preferStructured">Whether to prefer structured JSON output.</param>
        /// <param name="httpExec">Optional HTTP resilience handler.</param>
        public ZaiGlmProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.IHttpResilience? httpExec = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Z.AI API key is required", nameof(apiKey));

            _client = new ZaiGlmClient(httpClient, logger, apiKey, httpExec);
            _model = ZaiGlmModels.ToRawId(model ?? BrainarrConstants.DefaultZaiGlmModel);
            _preferStructured = preferStructured;

            _logger.Info($"Initialized Z.AI GLM provider with model: {_model}");
            if (httpExec == null)
            {
                try { _logger.Warn("ZaiGlmProvider: IHttpResilience not injected; using static resilience fallback"); }
                catch { /* Non-critical */ }
            }
        }

        /// <summary>
        /// Gets music recommendations based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt describing the user's music library and preferences.</param>
        /// <returns>A list of recommended albums with metadata.</returns>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
                TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await GetRecommendationsAsync(prompt, cts.Token);
        }

        /// <summary>
        /// Gets music recommendations based on the provided prompt with cancellation support.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Recommendations.</returns>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, CancellationToken cancellationToken)
        {
            try
            {
                var modelRaw = ZaiGlmModels.ToRawId(_model);
                var response = await SendWithFormatFallbackAsync(prompt, modelRaw, cancellationToken);

                if (response == null)
                {
                    _logger.Error("Z.AI GLM request failed with no HTTP response");
                    return new List<Recommendation>();
                }

                // Check for error in response body (even on HTTP 200)
                if (ZaiGlmErrorMapper.HasErrorInBody(response.Content))
                {
                    var error = ZaiGlmErrorMapper.MapError((int)response.StatusCode, response.Content);
                    _logger.Error($"Z.AI GLM API error: {error.UserMessage}");
                    return new List<Recommendation>();
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    _logger.Error($"Z.AI GLM API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                // Check finish_reason for content filtering
                if (ZaiGlmMapper.IsContentFiltered(response.Content))
                {
                    _logger.Warn("Z.AI GLM response was filtered due to content policy");
                    return new List<Recommendation>();
                }

                // Log token usage
                ZaiGlmMapper.LogUsage(response.Content, _logger);

                // Map response to recommendations
                var recommendations = ZaiGlmMapper.MapToRecommendations(response.Content, _logger);

                if (recommendations.Count == 0)
                {
                    _logger.Warn("Empty response from Z.AI GLM");
                }

                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Z.AI GLM");
                return new List<Recommendation>();
            }
        }

        /// <summary>
        /// Tests the connection to the Z.AI GLM API.
        /// </summary>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        public async Task<bool> TestConnectionAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
                TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout)));
            return await TestConnectionAsync(cts.Token);
        }

        /// <summary>
        /// Tests the connection to the Z.AI GLM API with cancellation support.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the connection is successful; otherwise, false.</returns>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var modelRaw = ZaiGlmModels.ToRawId(_model);
                var response = await _client.SendTestConnectionAsync(modelRaw, cancellationToken);

                var success = ZaiGlmClient.IsSuccessResponse(response);
                _logger.Info($"Z.AI GLM connection test: {(success ? "Success" : $"Failed with {response?.StatusCode}")}");

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Z.AI GLM connection test failed");
                return false;
            }
        }

        /// <summary>
        /// Updates the model used by the provider.
        /// </summary>
        /// <param name="modelName">The new model name to use.</param>
        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = ZaiGlmModels.ToRawId(modelName);
                _logger.Info($"Z.AI GLM model updated to: {_model}");
            }
        }

        #region Private Methods

        /// <summary>
        /// Sends request with format preference fallback (structured -> text -> bare).
        /// </summary>
        private async Task<HttpResponse?> SendWithFormatFallbackAsync(
            string prompt,
            string modelRaw,
            CancellationToken cancellationToken)
        {
            var artistOnly = PromptShapeHelper.IsArtistOnly(prompt);
            var systemContent = GetSystemPrompt(artistOnly);
            var temperature = TemperaturePolicy.FromPrompt(prompt, 1.0); // GLM default is 1.0

            var cacheKey = $"ZaiGlm:{modelRaw}";
            var preferStructuredNow = FormatPreferenceCache.GetPreferStructuredOrDefault(cacheKey, _preferStructured);

            var attempts = ChatRequestFactory.BuildBodies(
                AIProvider.ZaiGlm,
                modelRaw,
                systemContent,
                prompt,
                temperature,
                2000,
                preferStructured: preferStructuredNow);

            HttpResponse? response = null;
            var idx = 0;
            var usedIndex = -1;

            foreach (var body in attempts)
            {
                response = await _client.SendChatCompletionAsync(body, modelRaw, cancellationToken);
                if (response == null)
                {
                    idx++;
                    continue;
                }

                var code = (int)response.StatusCode;
                // If bad request or unprocessable, try next format
                if (response.StatusCode == HttpStatusCode.BadRequest || code == 422)
                {
                    idx++;
                    continue;
                }

                usedIndex = idx;
                break;
            }

            // Cache whether structured format worked
            FormatPreferenceCache.SetPreferStructured(cacheKey, usedIndex == 0 && preferStructuredNow);

            return response;
        }

        /// <summary>
        /// Gets the system prompt for recommendation requests.
        /// </summary>
        private static string GetSystemPrompt(bool artistOnly)
        {
            return artistOnly
                ? "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, genre, confidence (0-1), and reason. Focus on diverse, high-quality artist recommendations. Do not include album or year fields."
                : "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Focus on diverse, high-quality album recommendations that match the user's taste.";
        }

        #endregion
    }
}
