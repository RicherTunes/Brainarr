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
    public class OpenRouterProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = "https://openrouter.ai/api/v1/chat/completions";

        public string ProviderName => "OpenRouter";

        public OpenRouterProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "anthropic/claude-3.5-haiku")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenRouter API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "anthropic/claude-3.5-haiku"; // Default to cost-effective Claude model
            
            _logger.Info($"Initialized OpenRouter provider with model: {_model}");
        }

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
                    // OpenRouter specific parameters
                    transforms = new[] { "middle-out" }, // Optimize for balanced performance
                    route = "fallback" // Enable automatic fallback to similar models if primary is unavailable
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("HTTP-Referer", "https://github.com/brainarr/lidarr-plugin") // Required by OpenRouter
                    .SetHeader("X-Title", "Brainarr Music Recommendations") // Optional but recommended
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"OpenRouter API error: {response.StatusCode} - {response.Content}");
                    
                    // Log specific error if available
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(response.Content);
                        if (errorResponse?.Error != null)
                        {
                            _logger.Error($"OpenRouter error details: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code})");
                        }
                    }
                    catch { }
                    
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<OpenRouterResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from OpenRouter");
                    return new List<Recommendation>();
                }

                // Log the model that actually handled the request (might differ due to fallback)
                if (!string.IsNullOrEmpty(responseData?.Model))
                {
                    _logger.Debug($"Request handled by model: {responseData.Model}");
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from OpenRouter");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Use a minimal model for connection test to save costs
                var requestBody = new
                {
                    model = "openai/gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("HTTP-Referer", "https://github.com/brainarr/lidarr-plugin")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"OpenRouter connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "OpenRouter connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Try to parse as JSON array directly
                if (content.TrimStart().StartsWith("["))
                {
                    var parsed = JsonConvert.DeserializeObject<List<dynamic>>(content);
                    foreach (var item in parsed)
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                // Try to extract JSON array from the response
                else
                {
                    var jsonStart = content.IndexOf('[');
                    var jsonEnd = content.LastIndexOf(']');
                    
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var json = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                        var parsed = JsonConvert.DeserializeObject<List<dynamic>>(json);
                        
                        foreach (var item in parsed)
                        {
                            ParseSingleRecommendation(item, recommendations);
                        }
                    }
                    else
                    {
                        _logger.Warn("No JSON array found in OpenRouter response");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse OpenRouter recommendations");
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

        // Response models
        private class OpenRouterResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("model")]
            public string Model { get; set; }
            
            [JsonProperty("object")]
            public string Object { get; set; }
            
            [JsonProperty("created")]
            public long Created { get; set; }
            
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

        private class OpenRouterError
        {
            [JsonProperty("error")]
            public ErrorDetail Error { get; set; }
        }

        private class ErrorDetail
        {
            [JsonProperty("message")]
            public string Message { get; set; }
            
            [JsonProperty("type")]
            public string Type { get; set; }
            
            [JsonProperty("code")]
            public string Code { get; set; }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"OpenRouter model updated to: {modelName}");
            }
        }
    }
}