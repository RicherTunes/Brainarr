using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.ZaiGlm
{
    /// <summary>
    /// Maps Z.AI GLM API responses to Brainarr recommendation models.
    /// Handles response parsing, content extraction, and usage logging.
    /// </summary>
    public static class ZaiGlmMapper
    {
        /// <summary>
        /// Extracts the content string from a Z.AI GLM response.
        /// </summary>
        /// <param name="responseBody">The raw JSON response body.</param>
        /// <returns>The content text, or null if not found.</returns>
        public static string? ExtractContent(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
                return null;

            try
            {
                var response = JsonConvert.DeserializeObject<ZaiGlmResponse>(responseBody);
                return response?.Choices?.FirstOrDefault()?.Message?.Content;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the finish reason from a Z.AI GLM response.
        /// </summary>
        /// <param name="responseBody">The raw JSON response body.</param>
        /// <returns>The finish reason (e.g., "stop", "sensitive"), or null.</returns>
        public static string? GetFinishReason(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
                return null;

            try
            {
                var response = JsonConvert.DeserializeObject<ZaiGlmResponse>(responseBody);
                return response?.Choices?.FirstOrDefault()?.FinishReason;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if the response was filtered due to content policy.
        /// </summary>
        /// <param name="responseBody">The raw JSON response body.</param>
        /// <returns>True if the content was filtered.</returns>
        public static bool IsContentFiltered(string responseBody)
        {
            return GetFinishReason(responseBody) == "sensitive";
        }

        /// <summary>
        /// Maps a Z.AI GLM response to a list of recommendations.
        /// </summary>
        /// <param name="responseBody">The raw JSON response body.</param>
        /// <param name="logger">Logger for diagnostic output.</param>
        /// <returns>A list of parsed recommendations.</returns>
        public static List<Recommendation> MapToRecommendations(string responseBody, Logger logger)
        {
            var content = ExtractContent(responseBody);
            if (string.IsNullOrEmpty(content))
            {
                return new List<Recommendation>();
            }

            return RecommendationJsonParser.Parse(content, logger);
        }

        /// <summary>
        /// Logs token usage from a Z.AI GLM response.
        /// </summary>
        /// <param name="responseBody">The raw JSON response body.</param>
        /// <param name="logger">Logger for diagnostic output.</param>
        public static void LogUsage(string responseBody, Logger logger)
        {
            if (string.IsNullOrEmpty(responseBody))
                return;

            try
            {
                var response = JsonConvert.DeserializeObject<ZaiGlmResponse>(responseBody);
                if (response?.Usage != null)
                {
                    logger.Debug($"Z.AI GLM token usage - Prompt: {response.Usage.PromptTokens}, " +
                                $"Completion: {response.Usage.CompletionTokens}, " +
                                $"Total: {response.Usage.TotalTokens}");
                }
            }
            catch
            {
                // Non-critical - don't fail on usage logging
            }
        }

        #region Response Models

        /// <summary>
        /// Z.AI GLM API response structure.
        /// </summary>
        internal class ZaiGlmResponse
        {
            [JsonProperty("id")]
            public string? Id { get; set; }

            [JsonProperty("request_id")]
            public string? RequestId { get; set; }

            [JsonProperty("created")]
            public long Created { get; set; }

            [JsonProperty("model")]
            public string? Model { get; set; }

            [JsonProperty("choices")]
            public List<ZaiGlmChoice>? Choices { get; set; }

            [JsonProperty("usage")]
            public ZaiGlmUsage? Usage { get; set; }
        }

        /// <summary>
        /// Choice structure in Z.AI GLM response.
        /// </summary>
        internal class ZaiGlmChoice
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("message")]
            public ZaiGlmMessage? Message { get; set; }

            /// <summary>
            /// Finish reason: "stop", "length", "sensitive", "tool_calls", "network_error"
            /// </summary>
            [JsonProperty("finish_reason")]
            public string? FinishReason { get; set; }
        }

        /// <summary>
        /// Message structure in Z.AI GLM response.
        /// </summary>
        internal class ZaiGlmMessage
        {
            [JsonProperty("role")]
            public string? Role { get; set; }

            [JsonProperty("content")]
            public string? Content { get; set; }

            /// <summary>
            /// Chain-of-thought output from thinking mode (optional).
            /// </summary>
            [JsonProperty("reasoning_content")]
            public string? ReasoningContent { get; set; }
        }

        /// <summary>
        /// Token usage statistics from Z.AI GLM response.
        /// </summary>
        internal class ZaiGlmUsage
        {
            [JsonProperty("prompt_tokens")]
            public int PromptTokens { get; set; }

            [JsonProperty("completion_tokens")]
            public int CompletionTokens { get; set; }

            [JsonProperty("total_tokens")]
            public int TotalTokens { get; set; }
        }

        #endregion
    }
}
