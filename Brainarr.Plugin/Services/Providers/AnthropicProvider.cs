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
    public class AnthropicProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private readonly string _model;
        private const string API_URL = "https://api.anthropic.com/v1/messages";
        private const string ANTHROPIC_VERSION = "2023-06-01";

        public string ProviderName => "Anthropic";

        public AnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "claude-3-5-haiku-latest")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Anthropic API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "claude-3-5-haiku-latest"; // Default to latest Haiku for cost-effectiveness
            
            _logger.Info($"Initialized Anthropic provider with model: {_model}");
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
                            role = "user", 
                            content = $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

Rules:
1. Return ONLY a JSON array of recommendations
2. Each recommendation must have these fields: artist, album, genre, confidence (0-1), reason
3. Provide diverse, high-quality recommendations
4. Focus on albums that match the user's taste but expand their horizons

User request:
{prompt}

Respond with only the JSON array, no other text."
                        }
                    },
                    max_tokens = 2000,
                    temperature = 0.8,
                    system = "You are a knowledgeable music recommendation assistant. Always respond with valid JSON containing music recommendations."
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Anthropic API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<AnthropicResponse>(response.Content);
                var content = responseData?.Content?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Anthropic");
                    return new List<Recommendation>();
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Anthropic");
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
                        new { role = "user", content = "Reply with 'OK'" }
                    },
                    max_tokens = 10
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("x-api-key", _apiKey)
                    .SetHeader("anthropic-version", ANTHROPIC_VERSION)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Anthropic connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Anthropic connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Try to extract JSON array from the response
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
                }
                else
                {
                    _logger.Warn("No JSON array found in Anthropic response");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse Anthropic recommendations");
            }
            
            return recommendations;
        }

        // Response models
        private class AnthropicResponse
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("type")]
            public string Type { get; set; }
            
            [JsonProperty("role")]
            public string Role { get; set; }
            
            [JsonProperty("model")]
            public string Model { get; set; }
            
            [JsonProperty("content")]
            public List<ContentBlock> Content { get; set; }
            
            [JsonProperty("stop_reason")]
            public string StopReason { get; set; }
            
            [JsonProperty("stop_sequence")]
            public string StopSequence { get; set; }
            
            [JsonProperty("usage")]
            public Usage Usage { get; set; }
        }

        private class ContentBlock
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            
            [JsonProperty("text")]
            public string Text { get; set; }
        }

        private class Usage
        {
            [JsonProperty("input_tokens")]
            public int InputTokens { get; set; }
            
            [JsonProperty("output_tokens")]
            public int OutputTokens { get; set; }
        }
    }
}