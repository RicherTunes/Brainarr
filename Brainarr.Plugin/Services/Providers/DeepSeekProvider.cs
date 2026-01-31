using System;
using System.Collections.Generic;
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
    /// DeepSeek AI provider using OpenAI-compatible API format.
    /// </summary>
    public class DeepSeekProvider : HttpChatProviderBase
    {
        public override string ProviderName => "DeepSeek";
        protected override string DefaultModel => BrainarrConstants.DefaultDeepSeekModel;
        protected override string ApiUrl => BrainarrConstants.DeepSeekChatCompletionsUrl;

        public DeepSeekProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            IHttpResilience? httpResilience = null)
            : base(httpClient, logger, apiKey, model, preferStructured, httpResilience)
        {
            Logger.Info($"Initialized DeepSeek provider with model: {Model}");
        }

        protected override void ConfigureHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Authorization", $"Bearer {ApiKey}");
        }

        protected override object CreateRequestBody(string systemPrompt, string userPrompt, int maxTokens, double temperature)
        {
            var modelId = ModelIdMapper.ToRawId("deepseek", Model);

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
                Logger.Debug(ex, "Failed to parse DeepSeek response");
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
                    var total = usage["total_tokens"]?.ToObject<int>() ?? 0;
                    Logger.Debug($"DeepSeek token usage - Prompt: {prompt}, Completion: {completion}, Total: {total}");
                }
            }
            catch (JsonException)
            {
                // Non-critical - token usage logging is best-effort
            }
        }
    }
}
