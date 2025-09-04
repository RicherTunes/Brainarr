using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Brainarr.Plugin.Services.Security;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

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
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var text = artistOnly
                    ? $@"You are a music recommendation expert. Based on the user's music library and preferences, provide artist recommendations.

IMPORTANT: Return ONLY a JSON array with NO additional text, markdown, or explanations.

Each recommendation must have these exact fields:
- artist: The artist name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Example format:
[
  {{""artist"": ""Pink Floyd"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Iconic progressive artists""}}
]

User request:
{prompt}"
                    : $@"You are a music recommendation expert. Based on the user's music library and preferences, provide album recommendations.

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
{prompt}";

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
                                    text = text
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
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.DefaultAITimeout);

                var response = await _httpClient.ExecuteAsync(request);
                
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var endpoint = $"{API_BASE_URL}/{_model}:generateContent";
                        var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini endpoint: {endpoint} (key hidden)");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini request JSON: {snippet}");
                    }
                    catch { }
                }
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Google Gemini API error: {response.StatusCode}");
                    var body = response.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(body))
                    {
                        var snippet = body.Substring(0, Math.Min(body.Length, 500));
                        _logger.Debug($"Gemini API error body (truncated): {snippet}");
                    }
                    
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
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini response content: {snippet}");
                    }
                    catch { }
                }
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Google Gemini");
                    return new List<Recommendation>();
                }
                
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.Info($"[Brainarr Debug] Gemini response content: {snippet}");
                        if (responseData?.UsageMetadata != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Gemini usage: prompt={responseData.UsageMetadata.PromptTokenCount}, completion={responseData.UsageMetadata.CandidatesTokenCount}, total={responseData.UsageMetadata.TotalTokenCount}");
                        }
                    }
                    catch { }
                }
                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Google Gemini");
                return new List<Recommendation>();
            }
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetRecommendationsAsync(prompt);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
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
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

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

        public async Task<bool> TestConnectionAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ok = await TestConnectionAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return ok;
        }

        // Parsing centralized in RecommendationJsonParser

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
