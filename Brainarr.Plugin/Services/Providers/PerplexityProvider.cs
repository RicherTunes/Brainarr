using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Perplexity AI provider with online search capabilities.
    /// Uses OpenAI-compatible API format with enhanced parsing for citation markers.
    /// </summary>
    public class PerplexityProvider : HttpChatProviderBase
    {
        private const string API_URL = "https://api.perplexity.ai/chat/completions";

        public override string ProviderName => "Perplexity";
        protected override string DefaultModel => "llama-3.1-sonar-large-128k-online";
        protected override string ApiUrl => API_URL;

        public PerplexityProvider(
            IHttpClient httpClient,
            Logger logger,
            string apiKey,
            string? model = null,
            bool preferStructured = true,
            IHttpResilience? httpResilience = null)
            : base(httpClient, logger, apiKey, model, preferStructured, httpResilience)
        {
            Logger.Info($"Initialized Perplexity provider with model: {Model}");
        }

        protected override void ConfigureHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Authorization", $"Bearer {ApiKey}");
            builder.SetHeader("Accept", "application/json");
        }

        protected override object CreateRequestBody(string systemPrompt, string userPrompt, int maxTokens, double temperature)
        {
            var modelId = ModelIdMapper.ToRawId("perplexity", Model);

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
                var content = json["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(content))
                    return null;

                // Remove citation markers like [1] [12] which can precede JSON
                content = Regex.Replace(content, @"\[\d{1,3}\]", string.Empty);
                // Remove common code fences
                content = content.Replace("```json", string.Empty).Replace("```", string.Empty);

                return content;
            }
            catch (JsonException ex)
            {
                Logger.Debug(ex, "Failed to parse Perplexity response");
                return null;
            }
        }

        protected override List<Recommendation> ParseRecommendations(string content)
        {
            // First try lenient parsing (handles Perplexity's { recommendations: [...] } format)
            var result = TryParseLenient(content);
            if (result.Count > 0)
                return result;

            // Fallback to standard parsing
            return base.ParseRecommendations(content);
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
                    Logger.Debug($"Perplexity usage - Prompt: {prompt}, Completion: {completion}, Total: {total}");
                }
            }
            catch (JsonException)
            {
                // Non-critical - token usage logging is best-effort
            }
        }

        /// <summary>
        /// Lenient JSON parsing that handles Perplexity's { recommendations: [...] } format
        /// and case-insensitive property matching.
        /// </summary>
        private static List<Recommendation> TryParseLenient(string text)
        {
            var list = new List<Recommendation>();
            if (string.IsNullOrWhiteSpace(text)) return list;

            try
            {
                var tok = JToken.Parse(text);

                if (tok.Type == JTokenType.Object)
                {
                    var obj = (JObject)tok;
                    var recs = obj["recommendations"] ?? obj.Property("recommendations")?.Value;
                    if (recs is JArray arr)
                    {
                        foreach (var it in arr) MapRec(it, list);
                    }
                    else
                    {
                        MapRec(obj, list);
                    }
                }
                else if (tok is JArray arr)
                {
                    foreach (var it in arr) MapRec(it, list);
                }
            }
            catch (JsonException)
            {
                // Non-critical
            }

            // Apply provider-level defaults
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(list[i].Genre))
                {
                    list[i] = list[i] with { Genre = "Unknown" };
                }
            }

            return list;
        }

        private static void MapRec(JToken it, List<Recommendation> list)
        {
            if (it?.Type != JTokenType.Object) return;
            var obj = (JObject)it;

            string GetStr(string name)
            {
                var p = obj.Property(name) ?? obj.Properties().FirstOrDefault(pr =>
                    string.Equals(pr.Name, name, StringComparison.OrdinalIgnoreCase));
                return p?.Value?.Type == JTokenType.String ? (string?)p.Value : null;
            }

            double GetDouble(string name)
            {
                var p = obj.Property(name) ?? obj.Properties().FirstOrDefault(pr =>
                    string.Equals(pr.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p == null) return 0.85;
                if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer)
                    return (double)p.Value;
                if (p.Value.Type == JTokenType.String && double.TryParse((string?)p.Value, out var d)) return d;
                return 0.85;
            }

            var artist = GetStr("artist");
            if (string.IsNullOrWhiteSpace(artist)) return;

            list.Add(new Recommendation
            {
                Artist = artist,
                Album = GetStr("album") ?? string.Empty,
                Genre = GetStr("genre"),
                Reason = GetStr("reason"),
                Confidence = GetDouble("confidence")
            });
        }
    }
}
