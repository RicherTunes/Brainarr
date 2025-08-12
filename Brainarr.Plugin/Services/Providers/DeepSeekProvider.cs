using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class DeepSeekProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly string _model;
        private const string API_URL = "https://api.deepseek.com/v1/chat/completions";

        public string ProviderName => "DeepSeek";

        public DeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "deepseek-chat")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("DeepSeek API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "deepseek-chat"; // Default to DeepSeek V3
            
            _logger.Info($"Initialized DeepSeek provider with model: {_model}");
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
                            content = "You are a music recommendation expert. Always return recommendations in JSON format with fields: artist, album, genre, confidence (0-1), and reason. Focus on diverse, high-quality album recommendations that match the user's taste." 
                        },
                        new 
                        { 
                            role = "user", 
                            content = prompt 
                        }
                    },
                    temperature = 0.7,
                    max_tokens = 2000,
                    stream = false,
                    // DeepSeek specific: better JSON output
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
                    _logger.Error($"DeepSeek API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<DeepSeekResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from DeepSeek");
                    return new List<Recommendation>();
                }

                // Log token usage for cost tracking
                if (responseData?.Usage != null)
                {
                    _logger.Debug($"DeepSeek token usage - Prompt: {responseData.Usage.PromptTokens}, Completion: {responseData.Usage.CompletionTokens}, Total: {responseData.Usage.TotalTokens}");
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from DeepSeek");
                return new List<Recommendation>();
            }
        }

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
                _logger.Info($"DeepSeek connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "DeepSeek connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // DeepSeek with response_format should return valid JSON
                var jsonObj = JsonConvert.DeserializeObject<dynamic>(content);
                
                // Check if recommendations is an array property
                if (jsonObj?.recommendations != null)
                {
                    foreach (var item in jsonObj.recommendations)
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                // Or if the response contains an albums array
                else if (jsonObj?.albums != null)
                {
                    foreach (var item in jsonObj.albums)
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
                    _logger.Warn("Unexpected JSON structure in DeepSeek response");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse DeepSeek recommendations");
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

        // Response models (OpenAI-compatible format)
        private class DeepSeekResponse
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
            
            [JsonProperty("system_fingerprint")]
            public string SystemFingerprint { get; set; }
        }

        private class Choice
        {
            [JsonProperty("index")]
            public int Index { get; set; }
            
            [JsonProperty("message")]
            public Message Message { get; set; }
            
            [JsonProperty("logprobs")]
            public object LogProbs { get; set; }
            
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
            
            [JsonProperty("prompt_cache_hit_tokens")]
            public int? PromptCacheHitTokens { get; set; }
            
            [JsonProperty("prompt_cache_miss_tokens")]
            public int? PromptCacheMissTokens { get; set; }
        }
    }
}