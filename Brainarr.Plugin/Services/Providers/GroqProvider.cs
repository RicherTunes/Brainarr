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
    public class GroqProvider : IAIProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly string _apiKey;
        private string _model;
        private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";

        public string ProviderName => "Groq";

        public GroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "llama-3.3-70b-versatile")
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("Groq API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "llama-3.3-70b-versatile"; // Default to latest Llama
            
            _logger.Info($"Initialized Groq provider with model: {_model} (Ultra-fast inference)");
        }

        public async Task<List<Recommendation>> GetRecommendationsAsync(string prompt)
        {
            try
            {
                var artistOnly = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing.PromptShapeHelper.IsArtistOnly(prompt);
                var systemContent = artistOnly
                    ? @"You are a music recommendation expert. Always return recommendations in JSON format.

Each recommendation MUST have these exact fields:
- artist: The artist name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Do NOT include album or year fields.
Return ONLY a JSON array, no other text. Example:
[{""artist"": ""Pink Floyd"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Iconic progressive artists""}]"
                    : @"You are a music recommendation expert. Always return recommendations in JSON format.

Each recommendation MUST have these exact fields:
- artist: The artist name
- album: The album name
- genre: The primary genre
- confidence: A number between 0 and 1
- reason: A brief reason for the recommendation

Return ONLY a JSON array, no other text. Example:
[{""artist"": ""Pink Floyd"", ""album"": ""Dark Side of the Moon"", ""genre"": ""Progressive Rock"", ""confidence"": 0.95, ""reason"": ""Classic album""}]";

                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new 
                        { 
                            role = "system", 
                            content = systemContent 
                        },
                        new 
                        { 
                            role = "user", 
                            content = prompt 
                        }
                    },
                    temperature = 0.7,
                    max_tokens = 2000,
                    top_p = 0.9,
                    stream = false,
                    // Groq-specific: optimize for JSON output
                    response_format = new { type = "json_object" }
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                var json = SecureJsonSerializer.Serialize(requestBody);
                request.SetContent(json);
                var seconds = TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout);
                request.RequestTimeout = TimeSpan.FromSeconds(seconds);

                // Track response time for Groq's ultra-fast inference
                var startTime = DateTime.UtcNow;
                var response = await _httpClient.ExecuteAsync(request);
                
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = json?.Length > 4000 ? (json.Substring(0, 4000) + "... [truncated]") : json;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Groq endpoint: {API_URL}");
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Groq request JSON: {snippet}");
                    }
                    catch { }
                }
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    _logger.Error($"Groq API error: {response.StatusCode} - {response.Content}");
                    return new List<Recommendation>();
                }

                _logger.Debug($"Groq response time: {responseTime}ms");

                var responseData = JsonConvert.DeserializeObject<GroqResponse>(response.Content);
                var content = responseData?.Choices?.FirstOrDefault()?.Message?.Content;
                if (DebugFlags.ProviderPayload)
                {
                    try
                    {
                        var snippet = content?.Length > 4000 ? (content.Substring(0, 4000) + "... [truncated]") : content;
                        _logger.InfoWithCorrelation($"[Brainarr Debug] Groq response content: {snippet}");
                        if (responseData?.Usage != null)
                        {
                            _logger.InfoWithCorrelation($"[Brainarr Debug] Groq usage: prompt={responseData.Usage.PromptTokens}, completion={responseData.Usage.CompletionTokens}, total={responseData.Usage.TotalTokens}");
                        }
                    }
                    catch { }
                }
                
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn("Empty response from Groq");
                    return new List<Recommendation>();
                }

                // Log usage for monitoring
                if (responseData?.Usage != null)
                {
                    _logger.Debug($"Groq usage - Prompt: {responseData.Usage.PromptTokens}, Completion: {responseData.Usage.CompletionTokens}, Queue: {responseData.Usage.QueueTime}ms, Total: {responseData.Usage.TotalTime}ms");
                }

                return RecommendationJsonParser.Parse(content, _logger);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting recommendations from Groq");
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
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Reply with OK" }
                    },
                    max_tokens = 5,
                    temperature = 0
                };

                var request = new HttpRequestBuilder(API_URL)
                    .SetHeader("Authorization", $"Bearer {_apiKey}")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                request.Method = HttpMethod.Post;
                request.SetContent(SecureJsonSerializer.Serialize(requestBody));
                request.RequestTimeout = TimeSpan.FromSeconds(BrainarrConstants.TestConnectionTimeout);

                var startTime = DateTime.UtcNow;
                var response = await _httpClient.ExecuteAsync(request);
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                
                var success = response.StatusCode == System.Net.HttpStatusCode.OK;
                _logger.Info($"Groq connection test: {(success ? $"Success ({responseTime}ms)" : $"Failed with {response.StatusCode}")}");
                
                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Groq connection test failed");
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

        // Response models (OpenAI-compatible format)
        private class GroqResponse
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
            
            [JsonProperty("x_groq")]
            public GroqMetadata XGroq { get; set; }
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
            
            [JsonProperty("queue_time")]
            public double? QueueTime { get; set; }
            
            [JsonProperty("prompt_time")]
            public double? PromptTime { get; set; }
            
            [JsonProperty("completion_time")]
            public double? CompletionTime { get; set; }
            
            [JsonProperty("total_time")]
            public double? TotalTime { get; set; }
        }

        private class GroqMetadata
        {
            [JsonProperty("id")]
            public string Id { get; set; }
        }

        public void UpdateModel(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
            {
                _model = modelName;
                _logger.Info($"Groq model updated to: {modelName}");
            }
        }
    }
}
