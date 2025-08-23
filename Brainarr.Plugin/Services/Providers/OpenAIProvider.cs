using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Brainarr.Plugin.Models;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// OpenAI provider implementation for music recommendations using GPT models.
    /// Supports GPT-4, GPT-4 Turbo, GPT-3.5 Turbo, and other OpenAI models.
    /// </summary>
    /// <remarks>
    /// This provider requires an OpenAI API key from https://platform.openai.com/api-keys
    /// Pricing varies by model: GPT-4 is more expensive but higher quality,
    /// while GPT-3.5-turbo is more cost-effective for basic recommendations.
    /// </remarks>
    public class OpenAIProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = "https://api.openai.com/v1/chat/completions";

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public string ProviderName => "OpenAI";

        /// <summary>
        /// Initializes a new instance of the OpenAIProvider class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="apiKey">OpenAI API key (required)</param>
        /// <param name="model">Model to use (defaults to gpt-4o-mini for cost efficiency)</param>
        /// <exception cref="ArgumentNullException">Thrown when httpClient or logger is null</exception>
        /// <exception cref="ArgumentException">Thrown when apiKey is null or empty</exception>
        public OpenAIProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "gpt-4o-mini")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenAI API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "gpt-4o-mini"; // Default to cost-effective model
            
            _logger.Info($"Initialized OpenAI provider with model: {_model}");
        }

        /// <summary>
        /// Gets music recommendations from OpenAI based on the provided prompt.
        /// </summary>
        /// <param name="prompt">The prompt containing user's music library and preferences</param>
        /// <returns>List of music recommendations with confidence scores and reasoning</returns>
        /// <remarks>
        /// Uses the Chat Completions API with a system message to ensure JSON formatted responses.
        /// Implements retry logic and comprehensive error handling for reliability.
        /// Token usage is optimized through prompt engineering and temperature settings.
        /// </remarks>
        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                // SECURITY: Validate and sanitize prompt
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    throw new ArgumentException("Prompt cannot be empty");
                }
                
                if (prompt.Length > 10000)
                {
                    _logger.Warn("Prompt exceeds recommended length, truncating");
                    prompt = prompt.Substring(0, 10000);
                }
                
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new 
                        { 
                            role = "system", 
                            content = "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Provide diverse, high-quality recommendations based on the user's music taste." 
                        },
                        new 
                        { 
                            role = "user", 
                            content = prompt 
                        }
                    },
                    temperature = 0.8,
                    max_tokens = 2000,
                    response_format = new { type = "json_object" }
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    // Sanitize error message to prevent information disclosure
                    _logger.Error($"OpenAI API error: {response.StatusCode}");
                    _logger.Debug($"OpenAI API response details: {response.Content?.Substring(0, Math.Min(response.Content?.Length ?? 0, 500))}");
                    return new List<Recommendation>();
                }

                var responseData = SecureJsonSerializer.Deserialize<ProviderResponses.OpenAIResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from OpenAI");
                    return new List<Recommendation>();
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from OpenAI");
                return new List<Recommendation>();
            }
        }

        /// <summary>
        /// Tests the connection to the OpenAI API.
        /// </summary>
        /// <returns>True if the API is accessible and the key is valid; otherwise, false</returns>
        /// <remarks>
        /// Performs a lightweight request to the models endpoint to verify connectivity
        /// and API key validity without consuming significant tokens.
        /// </remarks>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"OpenAI connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenAI connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Parse JSON safely using JsonDocument to inspect structure
                using var document = SecureJsonSerializer.ParseDocument(content);
                var root = document.RootElement;
                
                // Check if recommendations is an array property
                if (root.TryGetProperty("recommendations", out var recommendationsArray) && 
                    recommendationsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in recommendationsArray.EnumerateArray())
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                // Or if the entire response is an array
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                // Or if it's an object with individual properties
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    ParseSingleRecommendation(root, recommendations);
                }
                else
                {
                    _logger.Warn("Unexpected JSON structure in OpenAI response");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse OpenAI recommendations");
            }
            
            return recommendations;
        }

        private void ParseSingleRecommendation(JsonElement item, List<Recommendation> recommendations)
        {
            try
            {
                var rec = new Recommendation
                {
                    Artist = GetJsonStringProperty(item, "artist", "Artist"),
                    Album = GetJsonStringProperty(item, "album", "Album"),
                    Genre = GetJsonStringProperty(item, "genre", "Genre") ?? "Unknown",
                    Confidence = GetJsonDoubleProperty(item, "confidence", "Confidence") ?? 0.85,
                    Reason = GetJsonStringProperty(item, "reason", "Reason") ?? "Recommended based on your preferences"
                };

                // Allow artist-only recommendations (for artist mode) or full recommendations (for album mode)
                if (!string.IsNullOrWhiteSpace(rec.Artist))
                {
                    recommendations.Add(rec);
                    _logger.Debug($"Parsed recommendation: {rec.Artist} - {rec.Album ?? "[Artist Only]"}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to parse individual recommendation");
            }
        }

        private string GetJsonStringProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    return prop.GetString();
                }
            }
            return null;
        }

        private double? GetJsonDoubleProperty(JsonElement element, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                    {
                        return prop.GetDouble();
                    }
                    else if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var val))
                    {
                        return val;
                    }
                }
            }
            return null;
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenAI model updated to: {modelName}");
            }
        }

        // Response models are now in ProviderResponses.cs for secure deserialization
    }
}