using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Base class for AI providers that use OpenAI-compatible API format.
    /// This includes OpenAI, Groq, DeepSeek, OpenRouter, and other compatible providers.
    /// </summary>
    public abstract class OpenAICompatibleProvider : BaseCloudProvider
    {
        protected OpenAICompatibleProvider(IHttpClient httpClient, Logger logger, string apiKey, string model)
            : base(httpClient, logger, apiKey, model)
        {
        }

        /// <summary>
        /// Creates the standard OpenAI-compatible request body.
        /// </summary>
        protected override object CreateRequestBody(string prompt, int maxTokens = 2000)
        {
            return new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens,
                temperature = 0.7
            };
        }

        /// <summary>
        /// Configures the standard Authorization header for OpenAI-compatible APIs.
        /// </summary>
        protected override void ConfigureHeaders(HttpRequestBuilder builder)
        {
            builder.SetHeader("Authorization", $"Bearer {_apiKey}");
        }

        /// <summary>
        /// Parses the standard OpenAI-compatible response format.
        /// </summary>
        protected override List<Recommendation> ParseResponse(string responseContent)
        {
            try
            {
                _logger.Debug($"[{ProviderName}] Parsing response: {responseContent?.Substring(0, Math.Min(200, responseContent?.Length ?? 0))}...");

                var response = JsonConvert.DeserializeObject<OpenAICompatibleResponse>(responseContent);
                
                if (response?.Choices == null || !response.Choices.Any())
                {
                    _logger.Warn($"[{ProviderName}] No choices in response");
                    return new List<Recommendation>();
                }

                var content = response.Choices.First().Message?.Content;
                if (string.IsNullOrEmpty(content))
                {
                    _logger.Warn($"[{ProviderName}] Empty content in response");
                    return new List<Recommendation>();
                }

                return ParseRecommendationsFromContent(content);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{ProviderName}] Failed to parse response");
                return new List<Recommendation>();
            }
        }

        /// <summary>
        /// Parses recommendations from the message content.
        /// </summary>
        private List<Recommendation> ParseRecommendationsFromContent(string content)
        {
            var recommendations = new List<Recommendation>();

            try
            {
                // Try to find JSON array in the content
                var jsonStart = content.IndexOf('[');
                var jsonEnd = content.LastIndexOf(']');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    _logger.Debug($"[{ProviderName}] Extracted JSON: {jsonStr.Substring(0, Math.Min(300, jsonStr.Length))}...");

                    var items = JsonConvert.DeserializeObject<dynamic[]>(jsonStr);
                    
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            ParseSingleRecommendation(item, recommendations);
                        }
                    }
                }
                else
                {
                    _logger.Warn($"[{ProviderName}] No JSON array found in response content");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{ProviderName}] Failed to parse recommendations from content");
            }

            return recommendations;
        }
    }

    /// <summary>
    /// OpenAI-compatible response model.
    /// </summary>
    internal class OpenAICompatibleResponse
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
        public List<OpenAIChoice> Choices { get; set; }

        [JsonProperty("usage")]
        public OpenAIUsage Usage { get; set; }
    }

    /// <summary>
    /// OpenAI-compatible choice model.
    /// </summary>
    internal class OpenAIChoice
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public OpenAIMessage Message { get; set; }

        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }
    }

    /// <summary>
    /// OpenAI-compatible message model.
    /// </summary>
    internal class OpenAIMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }

    /// <summary>
    /// OpenAI-compatible usage model.
    /// </summary>
    internal class OpenAIUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }
}