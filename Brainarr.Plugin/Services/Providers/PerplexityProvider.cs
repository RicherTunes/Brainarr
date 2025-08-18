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
    public class PerplexityProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = "https://api.perplexity.ai/chat/completions";

        public string ProviderName => "Perplexity";

        public PerplexityProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "llama-3.1-sonar-large-128k-online")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Perplexity API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "llama-3.1-sonar-large-128k-online"; // Default to their best online model
            
            _logger.Info($"Initialized Perplexity provider with model: {_model}");
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
                        new { role = "system", content = "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, year (if known), confidence (0-1), and reason." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000,
                    stream = false
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .SetHeader("Accept", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Perplexity API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<PerplexityResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Perplexity");
                    return new List<Recommendation>();
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Perplexity");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Simple test with minimal prompt
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with 'OK'" }
                    },
                    max_tokens = 10
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Perplexity connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Perplexity connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Try to extract JSON from the response
                var jsonStart = content.IndexOf('[');
                var jsonEnd = content.LastIndexOf(']');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var parsed = JsonConvert.DeserializeObject<List<dynamic>>(json);
                    
                    foreach (var item in parsed)
                    {
                        try
                        {
                            var rec = new Recommendation
                            {
                                Artist = item.artist?.ToString() ?? item.Artist?.ToString(),
                                Album = item.album?.ToString() ?? item.Album?.ToString(),
                                Genre = item.genre?.ToString() ?? item.Genre?.ToString() ?? "Unknown",
                                Confidence = item.confidence != null ? (double)item.confidence : 0.8,
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
                }
                else
                {
                    _logger.Warn("No JSON array found in Perplexity response");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse Perplexity recommendations");
            }
            
            return recommendations;
        }

        // Response models
        private class PerplexityResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
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

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Perplexity model updated to: {modelName}");
            }
        }
    }
}