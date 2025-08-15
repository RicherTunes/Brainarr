using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class GroqProvider : BaseAIProvider
    {
        public override string ProviderName => "Groq";
        protected override string ApiUrl => BrainarrConstants.GroqApiUrl;

        public GroqProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : base(httpClient, logger, apiKey, model ?? BrainarrConstants.DefaultGroqModel)
        {
            _logger.Info($"Initialized Groq provider with model: {_model} (Ultra-fast inference)");
        }

        protected override string SystemPrompt => 
            "You are a music recommendation expert. Always return recommendations in JSON format with fields: " +
            "artist, album, genre, year (if known), confidence (0-1), and reason. " +
            "Provide diverse, high-quality recommendations based on the user's music taste. " +
            "Ensure variety across genres while maintaining relevance to the user's preferences.";

        protected override object BuildRequestBody(string prompt)
        {
            return new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = prompt }
                },
                temperature = BrainarrConstants.DefaultTemperature,
                max_tokens = BrainarrConstants.MaxTokensDefault,
                stream = false,
                // Groq supports JSON mode
                response_format = new { type = "json_object" }
            };
        }

        protected override string ExtractContentFromResponse(string responseContent)
        {
            try
            {
                var responseData = JObject.Parse(responseContent);
                
                // Log token usage for monitoring
                var usage = responseData["usage"];
                if (usage != null)
                {
                    _logger.Debug($"Groq token usage - Prompt: {usage["prompt_tokens"]}, " +
                                $"Completion: {usage["completion_tokens"]}, Total: {usage["total_tokens"]}");
                }
                
                // Log Groq's fast inference time if available
                var inferenceTime = responseData["x_groq"]?["inference_time"];
                if (inferenceTime != null)
                {
                    _logger.Debug($"Groq inference time: {inferenceTime}ms");
                }
                
                return responseData["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract content from Groq response");
                return null;
            }
        }

        public override async Task<List<string>> GetAvailableModelsAsync()
        {
            // Groq doesn't have a public models endpoint, return known models
            await Task.CompletedTask;
            return new List<string>
            {
                "llama-3.3-70b-versatile",    // Latest Llama 3.3 70B
                "llama-3.2-90b-vision-preview", // Vision-capable model
                "llama-3.2-11b-vision-preview", // Smaller vision model
                "llama-3.2-3b-preview",        // Fastest, smallest model
                "mixtral-8x7b-32768",          // Mixtral for reasoning
                "gemma2-9b-it",                // Google's Gemma 2
                "llama3-groq-70b-8192-tool-use-preview", // Tool-use optimized
                "llama3-groq-8b-8192-tool-use-preview"   // Smaller tool-use model
            };
        }
    }
}