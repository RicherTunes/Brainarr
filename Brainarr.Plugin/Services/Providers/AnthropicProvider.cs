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
    public class AnthropicProvider : BaseAIProvider
    {
        public override string ProviderName => "Anthropic";
        protected override string ApiUrl => BrainarrConstants.AnthropicApiUrl;

        public AnthropicProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : base(httpClient, logger, apiKey, model ?? BrainarrConstants.DefaultAnthropicModel)
        {
        }

        protected override string SystemPrompt => 
            "You are a music recommendation expert. Always return recommendations as a JSON array with fields: " +
            "artist, album, genre, year (if known), confidence (0-1), and reason. " +
            "Focus on discovering hidden gems and understanding musical connections.";

        protected override object BuildRequestBody(string prompt)
        {
            return new
            {
                model = _model,
                max_tokens = BrainarrConstants.MaxTokensLarge,
                temperature = BrainarrConstants.PreciseTemperature,
                system = SystemPrompt,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };
        }

        protected override HttpRequest BuildHttpRequest(object requestBody)
        {
            var request = new HttpRequestBuilder(ApiUrl)
                .SetHeader("Content-Type", "application/json")
                .SetHeader("Accept", "application/json")
                .SetHeader("anthropic-version", BrainarrConstants.AnthropicVersion)
                .SetHeader("x-api-key", _apiKey) // Anthropic uses x-api-key header
                .Build();

            request.Method = HttpMethod.Post;
            request.SetContent(JsonConvert.SerializeObject(requestBody));

            return request;
        }

        protected override string ExtractContentFromResponse(string responseContent)
        {
            try
            {
                var data = JObject.Parse(responseContent);
                
                // Claude's response structure
                var content = data["content"]?[0]?["text"]?.ToString();
                
                if (string.IsNullOrEmpty(content))
                {
                    // Check for error message
                    var error = data["error"]?["message"]?.ToString();
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.Error($"Anthropic API error: {error}");
                    }
                }

                return content;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract content from Anthropic response");
                return null;
            }
        }

        public override async Task<List<string>> GetAvailableModelsAsync()
        {
            // Anthropic doesn't have a models endpoint, return known models
            await Task.CompletedTask;
            return new List<string>
            {
                "claude-3-5-haiku-latest",
                "claude-3-5-sonnet-latest",
                "claude-3-opus-20240229"
            };
        }
    }
}