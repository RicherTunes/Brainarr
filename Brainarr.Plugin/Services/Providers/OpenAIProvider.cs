using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    public class OpenAIProvider : BaseAIProvider
    {
        public override string ProviderName => "OpenAI";
        protected override string ApiUrl => BrainarrConstants.OpenAIApiUrl;

        public OpenAIProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : base(httpClient, logger, apiKey, model ?? BrainarrConstants.DefaultOpenAIModel)
        {
        }

        protected override string SystemPrompt =>
            "You are a music recommendation expert. Always return recommendations in JSON format with fields: " +
            "artist, album, genre, year (if known), confidence (0-1), and reason. " +
            "Provide diverse, high-quality recommendations based on the user's music taste.";

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
                response_format = new { type = "json_object" }
            };
        }

        protected override string ExtractContentFromResponse(string responseContent)
        {
            try
            {
                var responseData = JObject.Parse(responseContent);
                return responseData["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract content from OpenAI response");
                return null;
            }
        }

        public override async Task<List<string>> GetAvailableModelsAsync()
        {
            // OpenAI doesn't provide a models endpoint for chat models easily
            // Return known models
            await Task.CompletedTask;
            return new List<string>
            {
                "gpt-4o-mini",
                "gpt-4o",
                "gpt-4-turbo",
                "gpt-3.5-turbo"
            };
        }
    }
}