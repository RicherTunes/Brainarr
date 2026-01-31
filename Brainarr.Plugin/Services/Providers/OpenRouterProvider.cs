using System;
using System.Linq;
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
    /// OpenRouter provider supporting multiple AI models through a unified gateway.
    /// Includes custom headers for tracking and supports SYSTEM_AVOID markers.
    /// </summary>
    public class OpenRouterProvider : HttpChatProviderBase
    {
        public override string ProviderName => "OpenRouter";
        protected override string DefaultModel => BrainarrConstants.DefaultOpenRouterModel;
        protected override string ApiUrl => BrainarrConstants.OpenRouterChatCompletionsUrl;
        protected override double DefaultTemperature => 0.8;

        public OpenRouterProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            IHttpResilience? httpResilience = null)
            : base(httpClient, logger, apiKey, model, preferStructured, httpResilience)
        {
            Logger.Info($"Initialized OpenRouter provider with model: {Model}");
        }

        protected override void ConfigureHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Authorization", $"Bearer {ApiKey}");
            builder.SetHeader("HTTP-Referer", BrainarrConstants.ProjectReferer);
            builder.SetHeader("X-Title", BrainarrConstants.OpenRouterTitle);
        }

        protected override object CreateRequestBody(string systemPrompt, string userPrompt, int maxTokens, double temperature)
        {
            var modelId = ModelIdMapper.ToRawId("openrouter", Model);

            // Handle SYSTEM_AVOID marker in prompt
            var (processedSystemPrompt, processedUserPrompt) = ProcessSystemAvoid(systemPrompt, userPrompt);

            return new
            {
                model = modelId,
                messages = new[]
                {
                    new { role = "system", content = processedSystemPrompt },
                    new { role = "user", content = processedUserPrompt }
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
                var model = json["model"]?.ToString();
                if (!string.IsNullOrEmpty(model))
                {
                    Logger.Debug($"Request handled by model: {model}");
                }
                return json["choices"]?[0]?["message"]?["content"]?.ToString();
            }
            catch (JsonException ex)
            {
                Logger.Debug(ex, "Failed to parse OpenRouter response");
                return null;
            }
        }

        protected override void CaptureUserHint(HttpResponse response)
        {
            base.CaptureUserHint(response);

            var status = (int)response.StatusCode;
            var content = response.Content ?? string.Empty;

            if (status == 401)
            {
                SetUserHint(
                    "Invalid OpenRouter API key. Ensure it starts with 'sk-or-' and is active: https://openrouter.ai/keys",
                    BrainarrConstants.DocsOpenRouterSection);
            }
            else if (status == 402 || content.IndexOf("payment", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetUserHint(
                    "OpenRouter requires payment/credit. Add credit or resolve billing: https://openrouter.ai/settings/billing",
                    BrainarrConstants.DocsOpenRouterSection);
            }
            else if (status == 429 || content.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SetUserHint(
                    "OpenRouter rate limit exceeded. Wait, reduce request frequency, or choose a cheaper/faster route.",
                    BrainarrConstants.DocsOpenRouterSection);
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
                    Logger.Debug($"OpenRouter usage - Prompt: {prompt}, Completion: {completion}, Total: {total}");
                }
            }
            catch (JsonException)
            {
                // Non-critical - token usage logging is best-effort
            }
        }

        /// <summary>
        /// Process optional SYSTEM_AVOID marker in the user prompt.
        /// Format: [[SYSTEM_AVOID:Name — reason|...]]
        /// </summary>
        private (string systemPrompt, string userPrompt) ProcessSystemAvoid(string systemPrompt, string userPrompt)
        {
            if (string.IsNullOrWhiteSpace(userPrompt) || !userPrompt.StartsWith("[[SYSTEM_AVOID:"))
                return (systemPrompt, userPrompt);

            try
            {
                var endIdx = userPrompt.IndexOf("]]", StringComparison.Ordinal);
                if (endIdx <= 0)
                    return (systemPrompt, userPrompt);

                var marker = userPrompt.Substring(0, endIdx + 2);
                var inner = marker.Substring("[[SYSTEM_AVOID:".Length, marker.Length - "[[SYSTEM_AVOID:".Length - 2);

                if (!string.IsNullOrWhiteSpace(inner))
                {
                    var names = inner.Split('|').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    if (names.Length > 0)
                    {
                        var avoidAppendix = " Additionally, do not recommend these entities under any circumstances: " + string.Join(", ", names) + ".";
                        systemPrompt += avoidAppendix;
                        Logger.Info($"[Brainarr Debug] Applied system avoid list (OpenRouter): {names.Length} names");
                    }
                }

                userPrompt = userPrompt.Substring(endIdx + 2).TrimStart();
            }
            catch (Exception)
            {
                // Non-critical - fallback to original prompts
            }

            return (systemPrompt, userPrompt);
        }
    }
}
