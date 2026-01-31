using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Groq AI provider using OpenAI-compatible API format with ultra-fast inference.
    /// </summary>
    public class GroqProvider : HttpChatProviderBase
    {
        public override string ProviderName => "Groq";
        protected override string DefaultModel => BrainarrConstants.DefaultGroqModel;
        protected override string ApiUrl => BrainarrConstants.GroqChatCompletionsUrl;

        public GroqProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            IHttpResilience? httpResilience = null)
            : base(httpClient, logger, apiKey, model, preferStructured, httpResilience)
        {
            Logger.Info($"Initialized Groq provider with model: {Model} (Ultra-fast inference)");
        }

        protected override void ConfigureHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Authorization", $"Bearer {ApiKey}");
        }

        protected override object CreateRequestBody(string systemPrompt, string userPrompt, int maxTokens, double temperature)
        {
            var modelId = ModelIdMapper.ToRawId("groq", Model);

            return new
            {
                model = modelId,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                max_tokens = maxTokens,
                temperature = temperature
            };
        }

        protected override string? ExtractContent(string responseBody)
        {
            try
            {
                var json = JObject.Parse(responseBody);
                return json["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (JsonException ex)
            {
                Logger.Debug(ex, "Failed to parse Groq response");
                return null;
            }
        }

        protected override void LogTokenUsage(string responseBody)
        {
            try
            {
                var json = JObject.Parse(responseBody);
                var usage = json["usage"];
                if (usage != null)
                {
                    var prompt = usage["prompt_tokens"]?.ToObject<int>() ?? 0;
                    var completion = usage["completion_tokens"]?.ToObject<int>() ?? 0;
                    var queueTime = usage["queue_time"]?.ToObject<double>();
                    var totalTime = usage["total_time"]?.ToObject<double>();

                    var timing = "";
                    if (queueTime.HasValue || totalTime.HasValue)
                    {
                        timing = $", Queue: {queueTime:F1}ms, Total: {totalTime:F1}ms";
                    }

                    Logger.Debug($"Groq usage - Prompt: {prompt}, Completion: {completion}{timing}");
                }
            }
            catch (JsonException)
            {
                // Non-critical - token usage logging is best-effort
            }
        }
    }
}
