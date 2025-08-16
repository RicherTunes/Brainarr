using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using Brainarr.Plugin.Services.Providers.Base;

namespace Brainarr.Plugin.Services.Providers.Refactored
{
    public class OpenAIProvider : BaseHttpProvider
    {
        private readonly string _apiKey;
        private readonly string _model;
        
        private const string OPENAI_API_URL = "https://api.openai.com/v1/chat/completions";
        
        public override string ProviderName => "OpenAI";
        protected override string ApiEndpoint => OPENAI_API_URL;
        protected override bool RequiresAuthentication => true;
        
        public OpenAIProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = "gpt-4o-mini")
            : base(httpClient, logger)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("OpenAI API key is required", nameof(apiKey));
            
            _apiKey = apiKey;
            _model = model ?? "gpt-4o-mini";
            
            _logger.Info($"Initialized OpenAI provider with model: {_model}");
        }
        
        protected override object BuildRequestPayload(string prompt)
        {
            return new
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
                temperature = 0.9,
                max_tokens = 4000,
                response_format = new { type = "json_object" }
            };
        }
        
        protected override void ConfigureAuthentication(HttpRequestBuilder requestBuilder)
        {
            requestBuilder.SetHeader("Authorization", $"Bearer {_apiKey}");
        }
        
        protected override async Task<List<Recommendation>> ParseProviderResponseAsync(string responseContent)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("choices", out var choices) && 
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        var recommendationJson = content.GetString();
                        if (!string.IsNullOrWhiteSpace(recommendationJson))
                        {
                            // Parse the JSON content from the message
                            using var recDoc = JsonDocument.Parse(recommendationJson);
                            var recRoot = recDoc.RootElement;
                            
                            // Look for recommendations array in the response
                            if (recRoot.TryGetProperty("recommendations", out var recsElement))
                            {
                                return JsonSerializer.Deserialize<List<Recommendation>>(
                                    recsElement.GetRawText(), 
                                    _jsonOptions);
                            }
                            
                            // Try to parse as direct array
                            if (recRoot.ValueKind == JsonValueKind.Array)
                            {
                                return JsonSerializer.Deserialize<List<Recommendation>>(
                                    recommendationJson, 
                                    _jsonOptions);
                            }
                        }
                    }
                }
                
                _logger.Warn("[OpenAI] Response structure not recognized, attempting generic parsing");
                return null; // Let base class handle generic parsing
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "[OpenAI] Provider-specific parsing failed");
                return null; // Let base class handle generic parsing
            }
        }
    }
}