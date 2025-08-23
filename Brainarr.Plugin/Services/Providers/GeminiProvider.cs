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
    public class GeminiProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";

        public string ProviderName => "Google Gemini";

        public GeminiProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "gemini-1.5-flash")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Google Gemini API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "gemini-1.5-flash"; // Default to Flash for speed
            
            _logger.Info($"Initialized Google Gemini provider with model: {_model}");
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new
                                {
                                    text = $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

IMPORTANT: Return ONLY a JSON array with NO additional text, markdown, or explanations.

Each recommendation must have these exact fields:
- artist: The artist name
- album: The album name  
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Example format:
[
  {{""artist"": ""Pink Floyd"", ""album"": ""The Dark Side of the Moon"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Classic progressive rock masterpiece""}}
]

User request:
{prompt}"
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.8,
                        topP = 0.95,
                        topK = 40,
                        maxOutputTokens = 2048,
                        responseMimeType = "application/json" // Gemini 1.5 supports JSON mode
                    },
                    safetySettings = new[]
                    {
                        new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                        new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
                    }
                };

                var url = $"{API_BASE_URL}/{_model}:generateContent?key={_apiKey}";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Google Gemini API error: {response.StatusCode} - {response.Content}");
                    
                    // Parse error if available
                    try
                    {
                        var errorResponse = JsonConvert.DeserializeObject<GeminiError>(response.Content);
                        if (errorResponse?.Error != null)
                        {
                            _logger.Error($"Gemini error: {errorResponse.Error.Message} (Code: {errorResponse.Error.Code}, Status: {errorResponse.Error.Status})");
                        }
                    }
                    catch { }
                    
                    return new List<Recommendation>();
                }

                var responseData = JsonConvert.DeserializeObject<GeminiResponse>(response.Content);
                var content = responseData?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Google Gemini");
                    return new List<Recommendation>();
                }

                return ParseRecommendations(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Google Gemini");
                return new List<Recommendation>();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = "Reply with OK" }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        maxOutputTokens = 10
                    }
                };

                var url = $"{API_BASE_URL}/{_model}:generateContent?key={_apiKey}";
                var request = new HttpRequestBuilder(url)
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(JsonConvert.SerializeObject(requestBody));

                var response = await _httpClient.ExecuteAsync(request);
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Google Gemini connection test: {(success ? "Success" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Google Gemini connection test failed");
                return false;
            }
        }

        private List<Recommendation> ParseRecommendations(string content)
        {
            var recommendations = new List<Recommendation>();
            
            try
            {
                // Gemini with responseMimeType="application/json" should return valid JSON
                if (content.TrimStart().StartsWith("["))
                {
                    var parsed = JsonConvert.DeserializeObject<List<dynamic>>(content);
                    foreach (var item in parsed)
                    {
                        ParseSingleRecommendation(item, recommendations);
                    }
                }
                else
                {
                    // Try to parse as object with recommendations array
                    var jsonObj = JsonConvert.DeserializeObject<dynamic>(content);
                    
                    if (jsonObj?.recommendations != null)
                    {
                        foreach (var item in jsonObj.recommendations)
                        {
                            ParseSingleRecommendation(item, recommendations);
                        }
                    }
                    else if (jsonObj?.albums != null)
                    {
                        foreach (var item in jsonObj.albums)
                        {
                            ParseSingleRecommendation(item, recommendations);
                        }
                    }
                    else
                    {
                        _logger.Warn("Unexpected JSON structure in Gemini response");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to parse Google Gemini recommendations");
                
                // Fallback: try to extract JSON from text
                try
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
                }
                catch (Exception ex2)
                {
                    _logger.Error(ex2, "Failed to extract JSON from Gemini response");
                }
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
        private class GeminiResponse
        {
            [JsonProperty("candidates")]
            public List<Candidate> Candidates { get; set; }
            
            [JsonProperty("usageMetadata")]
            public UsageMetadata UsageMetadata { get; set; }
        }

        private class Candidate
        {
            [JsonProperty("content")]
            public Content Content { get; set; }
            
            [JsonProperty("finishReason")]
            public string FinishReason { get; set; }
            
            [JsonProperty("safetyRatings")]
            public List<SafetyRating> SafetyRatings { get; set; }
        }

        private class Content
        {
            [JsonProperty("parts")]
            public List<Part> Parts { get; set; }
            
            [JsonProperty("role")]
            public string Role { get; set; }
        }

        private class Part
        {
            [JsonProperty("text")]
            public string Text { get; set; }
        }

        private class SafetyRating
        {
            [JsonProperty("category")]
            public string Category { get; set; }
            
            [JsonProperty("probability")]
            public string Probability { get; set; }
        }

        private class UsageMetadata
        {
            [JsonProperty("promptTokenCount")]
            public int PromptTokenCount { get; set; }
            
            [JsonProperty("candidatesTokenCount")]
            public int CandidatesTokenCount { get; set; }
            
            [JsonProperty("totalTokenCount")]
            public int TotalTokenCount { get; set; }
        }

        private class GeminiError
        {
            [JsonProperty("error")]
            public ErrorDetail Error { get; set; }
        }

        private class ErrorDetail
        {
            [JsonProperty("code")]
            public int Code { get; set; }
            
            [JsonProperty("message")]
            public string Message { get; set; }
            
            [JsonProperty("status")]
            public string Status { get; set; }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Gemini model updated to: {modelName}");
            }
        }
    }
}