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

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Abstract base class for cloud AI providers.
    /// Provides common functionality for HTTP communication, error handling, and response parsing.
    /// </summary>
    public abstract class BaseCloudProvider : IAIProvider
    {
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
            try
            {
                _logger.Info($"Getting recommendations from {ProviderName}");

                var requestBody = CreateRequestBody(prompt);
                var response = await ExecuteRequestAsync(requestBody);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"{ProviderName} API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var recommendations = ParseResponse(response.Content);
                _logger.Info($"Received {recommendations.Count} recommendations from {ProviderName}");
                
                return recommendations;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting recommendations from {ProviderName}");
                return new List<Recommendation>();
            }
        }

        /// <summary>
        /// Tests the connection to the AI provider.
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.Info($"Testing connection to {ProviderName}");

                var testBody = CreateRequestBody("Reply with OK", 5);
                var response = await ExecuteRequestAsync(testBody);

                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"{ProviderName} connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"{ProviderName} connection test failed");
                return false;
            }
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
        private async Task<HttpResponse> ExecuteRequestAsync(object requestBody)
        {
            var builder = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Content-Type", "application/json");

            ConfigureHeaders(builder);

            var request = builder.Build();
            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(requestBody));

            return await _httpClient.ExecuteAsync(request);
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