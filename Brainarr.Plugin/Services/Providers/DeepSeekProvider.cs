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
    public class DeepSeekProvider : BaseAIProvider
    {
        public override string ProviderName => "DeepSeek";
        protected override string ApiUrl => BrainarrConstants.DeepSeekApiUrl;

        public DeepSeekProvider(IHttpClient httpClient, Logger logger, string apiKey, string model = null)
            : base(httpClient, logger, apiKey, model ?? BrainarrConstants.DefaultDeepSeekModel)
        {
        }

        protected override string SystemPrompt =>
            "You are a music recommendation expert. Always return recommendations in JSON format with fields: " +
            "artist, album, genre, year (if known), confidence (0-1), and reason. " +
            "Focus on diverse, high-quality album recommendations that match the user's taste.";

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
                temperature = BrainarrConstants.PreciseTemperature,
                max_tokens = BrainarrConstants.MaxTokensDefault,
                stream = false,
                // DeepSeek specific: better JSON output
                response_format = new { type = "json_object" }
            };
        }

        protected override string ExtractContentFromResponse(string responseContent)
        {
            try
            {
                var responseData = JObject.Parse(responseContent);

                // Log token usage for cost tracking
                var usage = responseData["usage"];
                if (usage != null)
                {
                    _logger.Debug($"DeepSeek token usage - Prompt: {usage["prompt_tokens"]}, " +
                                $"Completion: {usage["completion_tokens"]}, Total: {usage["total_tokens"]}");
                }

                return responseData["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract content from DeepSeek response");
                return null;
            }
        }

        public override async Task<List<string>> GetAvailableModelsAsync()
        {
            // DeepSeek doesn't have a public models endpoint, return known models
            await Task.CompletedTask;
            return new List<string>
            {
                "deepseek-chat",       // Latest DeepSeek V3
                "deepseek-coder",      // Code-specialized model
                "deepseek-reasoner"    // Reasoning-specialized model
            };
        }
    }
}