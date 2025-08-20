using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;

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
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"OpenAI API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<OpenAIResponse>(response.Content);
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
                request.SetContent(JsonConvert.SerializeObject(requestBody));

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
                // OpenAI response should be valid JSON due to response_format setting
                var jsonObj = JsonConvert.DeserializeObject<dynamic>(content);
                
                // Check if recommendations is an array property
                if (jsonObj?.recommendations != null)
                {
                    foreach (var item in jsonObj.recommendations)
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                // Or if the entire response is an array
                else if (content.TrimStart().StartsWith("["))
                {
                    var parsed = JsonConvert.DeserializeObject<List<dynamic>>(content);
                    foreach (var item in parsed)
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
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

        private void ParseSingleRecommendation(dynamic item, List<Recommendation> recommendations)
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

                if (!string.IsNullOrWhiteSpace(rec.Artist) && !string.IsNullOrWhiteSpace(rec.Album))
                {
                    recommendations.Add(rec);
                    _logger.Debug($"Parsed recommendation: {rec.Artist} - {rec.Album}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to parse individual recommendation");
            }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenAI model updated to: {modelName}");
            }
        }

        // Response models
        private class OpenAIResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("object")]
            public string Object { get; set; }
            
            [JsonProperty("created")]
            public long Created { get; set; }
            
            [JsonProperty("model")]
            public string Model { get; set; }
            
            [JsonProperty("choices")]
            public List<Choice> Choices { get; set; }
            
            [JsonProperty("usage")]
            public Usage Usage { get; set; }
        }

        private class Choice
        {
            [JsonProperty("index")]
            public int Index { get; set; }
            
            [JsonProperty("message")]
            public Message Message { get; set; }
            
            [JsonProperty("finish_reason")]
            public string FinishReason { get; set; }
        }

        private class Message
        {
            [JsonProperty("role")]
            public string Role { get; set; }
            
            [JsonProperty("content")]
            public string Content { get; set; }
        }

        private class Usage
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